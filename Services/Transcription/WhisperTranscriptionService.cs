using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Whisper.net;
using Whisper.net.Ggml;

namespace VoxMemo.Services.Transcription;

public class WhisperTranscriptionService : ITranscriptionService
{
    // Global lock — ggml native library crashes if two Whisper instances run concurrently
    private static readonly SemaphoreSlim _whisperLock = new(1, 1);
    private static bool _runtimeInitialized;

    private readonly string _modelsDir;

    public event EventHandler<int>? ProgressChanged;
    public string EngineName => "whisper-local";

    public WhisperTranscriptionService()
    {
        _modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo", "models");
        Directory.CreateDirectory(_modelsDir);
        EnsureRuntimeLoaded();
    }

    /// <summary>
    /// Ensures the Whisper native library can be found by adding runtimes/ paths
    /// to the DLL search directories. Needed when running from bin/Debug on some .NET versions.
    /// </summary>
    private static void EnsureRuntimeLoaded()
    {
        if (_runtimeInitialized) return;
        _runtimeInitialized = true;

        var baseDir = AppContext.BaseDirectory;
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

        // Only search for libs that match the current platform — never copy Linux .so on Windows etc.
        string[] platformExts;
        string[] platformRids;
        if (OperatingSystem.IsWindows())
        {
            platformExts = ["*.dll"];
            platformRids = ["win-x64", "win-arm64", "win-x86"];
        }
        else if (OperatingSystem.IsLinux())
        {
            platformExts = ["*.so"];
            platformRids = ["linux-x64", "linux-arm64"];
        }
        else
        {
            platformExts = ["*.dylib"];
            platformRids = ["osx-arm64", "osx-x64"];
        }

        // Prefer exact RID first, then generic platform RIDs; check /native subdir before parent
        var candidateRids = new[] { rid }.Concat(platformRids).Distinct();
        var candidates = candidateRids.SelectMany(r => new[]
        {
            Path.Combine(baseDir, "runtimes", r, "native"),
            Path.Combine(baseDir, "runtimes", r),
        });

        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir)) continue;

            var files = platformExts.SelectMany(ext => Directory.GetFiles(dir, ext)).ToArray();
            if (files.Length == 0) continue;

            foreach (var file in files)
            {
                var dest = Path.Combine(baseDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    try
                    {
                        File.Copy(file, dest);
                        Log.Debug("Copied native lib {File} to {Dest}", Path.GetFileName(file), baseDir);
                    }
                    catch { }
                }
            }

            Log.Information("Whisper native libs resolved from: {Path}", dir);
            return;
        }

        Log.Warning("Could not find Whisper native libraries in runtimes/ folder");
    }

    public Task<List<string>> GetAvailableModelsAsync()
    {
        var models = new List<string>();
        if (Directory.Exists(_modelsDir))
        {
            models = Directory.GetFiles(_modelsDir, "ggml-*.bin")
                .Select(f => Path.GetFileNameWithoutExtension(f).Replace("ggml-", ""))
                .ToList();
        }
        return Task.FromResult(models);
    }

    public async Task DownloadModelAsync(string modelName, IProgress<float>? progress = null, CancellationToken ct = default)
    {
        var modelType = modelName.ToLower() switch
        {
            "tiny" => GgmlType.Tiny,
            "base" => GgmlType.Base,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            "large-v2" => GgmlType.LargeV2,
            "large" or "large-v3" => GgmlType.LargeV3,
            "large-v3-turbo" => GgmlType.LargeV3Turbo,
            _ => GgmlType.Base
        };

        var modelPath = Path.Combine(_modelsDir, $"ggml-{modelName}.bin");
        if (File.Exists(modelPath)) return;

        using var httpClient = new HttpClient();
        var downloader = new WhisperGgmlDownloader(httpClient);
        using var modelStream = await downloader.GetGgmlModelAsync(modelType, cancellationToken: ct);
        using var fileStream = File.Create(modelPath);

        var buffer = new byte[81920];
        int bytesRead;
        long totalRead = 0;

        while ((bytesRead = await modelStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress?.Report(totalRead);
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string language = "en",
        string? modelName = null,
        CancellationToken ct = default)
    {
        modelName ??= "base";
        var modelPath = Path.Combine(_modelsDir, $"ggml-{modelName}.bin");

        if (!File.Exists(modelPath))
        {
            Log.Error("Whisper model not found: {Model} at {Path}", modelName, modelPath);
            throw new FileNotFoundException($"Whisper model '{modelName}' not found. Please download it first.");
        }

        // Convert non-WAV files to Whisper-compatible WAV before transcribing
        string? tempWav = null;
        var ext = Path.GetExtension(audioPath).ToLower();
        if (ext != ".wav")
        {
            Log.Information("Converting {Ext} to WAV before transcription: {Path}", ext, audioPath);
            tempWav = Path.Combine(Path.GetTempPath(), $"voxmemo_convert_{Guid.NewGuid():N}.wav");
            Platform.PlatformServices.AudioConverter.ConvertToWhisperFormat(audioPath, tempWav);
            audioPath = tempWav;
        }

        // Acquire global lock — only one Whisper instance at a time
        Log.Debug("Waiting for Whisper lock (model={Model})...", modelName);
        await _whisperLock.WaitAsync(ct);
        try
        {
            Log.Information("Transcribing {AudioPath} with model={Model} language={Language}", audioPath, modelName, language);
            using var whisperFactory = WhisperFactory.FromPath(modelPath);

            var builder = whisperFactory.CreateBuilder();
            if (language != "auto" && !string.IsNullOrEmpty(language))
                builder = builder.WithLanguage(language);
            await using var processor = builder.Build();

            var segments = new List<TranscriptionSegment>();
            int segmentIndex = 0;

            await using var fileStream = File.OpenRead(audioPath);

            await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            {
                segments.Add(new TranscriptionSegment(
                    (long)segment.Start.TotalMilliseconds,
                    (long)segment.End.TotalMilliseconds,
                    segment.Text.Trim(),
                    segment.Probability));

                segmentIndex++;
                ProgressChanged?.Invoke(this, segmentIndex);
            }

            var fullText = string.Join(" ", segments.Select(s => s.Text));
            Log.Information("Transcription complete: {SegmentCount} segments, {Length} chars", segments.Count, fullText.Length);

            return new TranscriptionResult(
                fullText,
                segments,
                language,
                EngineName,
                modelName);
        }
        finally
        {
            _whisperLock.Release();
            Log.Debug("Whisper lock released (model={Model})", modelName);
            if (tempWav != null) try { File.Delete(tempWav); } catch { }
        }
    }
}
