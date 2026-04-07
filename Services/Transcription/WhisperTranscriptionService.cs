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

    private readonly string _modelsDir;

    public event EventHandler<int>? ProgressChanged;
    public string EngineName => "whisper-local";

    public WhisperTranscriptionService()
    {
        _modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo", "models");
        Directory.CreateDirectory(_modelsDir);
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
            "large" or "large-v3" => GgmlType.LargeV3,
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
            Audio.AudioConverter.ConvertToWhisperFormat(audioPath, tempWav);
            audioPath = tempWav;
        }

        // Acquire global lock — only one Whisper instance at a time
        Log.Debug("Waiting for Whisper lock (model={Model})...", modelName);
        await _whisperLock.WaitAsync(ct);
        try
        {
            Log.Information("Transcribing {AudioPath} with model={Model} language={Language}", audioPath, modelName, language);
            using var whisperFactory = WhisperFactory.FromPath(modelPath);

            await using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(language)
                .Build();

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
