using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;
using VoxMemo.Services.Database;

namespace VoxMemo.Services.Platform.Windows;

public static class WindowsRecordingRecoveryService
{
    private static readonly Regex TempFilePattern = new(
        @"^vox_(mic|sys)_([0-9a-f]{8})\.wav$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> RecoverInterruptedRecordings()
    {
        var recordingsDir = ResolveRecordingsDirectory();
        return RecoverInterruptedRecordings(recordingsDir);
    }

    public static IReadOnlyList<string> RecoverInterruptedRecordings(string recordingsDir)
    {
        var recovered = new List<string>();

        if (string.IsNullOrWhiteSpace(recordingsDir) || !Directory.Exists(recordingsDir))
            return recovered;

        foreach (var wavPath in Directory.EnumerateFiles(recordingsDir, "*.wav"))
            TryRepairWaveHeader(wavPath);

        var tempFiles = Directory.EnumerateFiles(recordingsDir, "vox_*.wav")
            .Select(path => new FileInfo(path))
            .Select(file => new
            {
                File = file,
                Match = TempFilePattern.Match(file.Name),
            })
            .Where(x => x.Match.Success)
            .GroupBy(
                x => x.Match.Groups[2].Value,
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in tempFiles)
        {
            var micPath = group
                .Where(x => x.Match.Groups[1].Value.Equals("mic", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.File.FullName)
                .FirstOrDefault();
            var sysPath = group
                .Where(x => x.Match.Groups[1].Value.Equals("sys", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.File.FullName)
                .FirstOrDefault();

            if (micPath == null && sysPath == null)
                continue;

            var recoveredPath = BuildRecoveredOutputPath(recordingsDir, group.Key, micPath, sysPath);
            if (TryRecoverRecording(micPath, sysPath, recoveredPath))
                recovered.Add(recoveredPath);
        }

        if (recovered.Count > 0)
            Log.Warning("Recovered {Count} interrupted recording(s) in {Directory}", recovered.Count, recordingsDir);

        return recovered;
    }

    public static bool TryRepairWaveHeader(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (stream.Length < 44)
                return false;

            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            if (new string(reader.ReadChars(4)) != "RIFF")
                return false;

            var storedRiffSize = reader.ReadUInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
                return false;

            long? dataSizeOffset = null;
            long? dataStartOffset = null;
            uint storedDataSize = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadUInt32();
                var chunkDataStart = stream.Position;

                if (chunkId == "data")
                {
                    dataSizeOffset = chunkDataStart - 4;
                    dataStartOffset = chunkDataStart;
                    storedDataSize = chunkSize;
                    break;
                }

                var nextOffset = chunkDataStart + chunkSize;
                if ((chunkSize & 1) != 0)
                    nextOffset++;

                if (nextOffset > stream.Length)
                    break;

                stream.Position = nextOffset;
            }

            if (dataSizeOffset == null || dataStartOffset == null)
                return false;

            var actualRiffSize = (uint)Math.Max(0, stream.Length - 8);
            var actualDataSize = (uint)Math.Max(0, stream.Length - dataStartOffset.Value);

            if (storedRiffSize == actualRiffSize && storedDataSize == actualDataSize)
                return true;

            stream.Position = 4;
            using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write(actualRiffSize);
            stream.Position = dataSizeOffset.Value;
            writer.Write(actualDataSize);
            writer.Flush();

            Log.Warning("Repaired incomplete WAV header for {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to repair WAV header for {Path}", path);
            return false;
        }
    }

    internal static bool TryRecoverRecording(string? micPath, string? sysPath, string outputPath)
        => TryCreateMixedRecording(micPath, sysPath, outputPath, cleanupInputsOnSuccess: true);

    internal static bool TryCreateMixedRecording(
        string? micPath,
        string? sysPath,
        string outputPath,
        bool cleanupInputsOnSuccess)
    {
        var micValid = IsRecoverableWave(micPath);
        var sysValid = IsRecoverableWave(sysPath);

        if (!micValid && !sysValid)
        {
            Log.Warning("No recoverable temp recordings found for {Output}", outputPath);
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            if (!micValid && sysPath != null)
            {
                File.Move(sysPath, outputPath, overwrite: true);
                Log.Warning("Recovered recording from system audio only: {Output}", outputPath);
            }
            else if (!sysValid && micPath != null)
            {
                File.Move(micPath, outputPath, overwrite: true);
                Log.Warning("Recovered recording from microphone audio only: {Output}", outputPath);
            }
            else
            {
                using var micReader = new AudioFileReader(micPath!);
                using var sysReader = new AudioFileReader(sysPath!);

                ISampleProvider micSamples = micReader;
                ISampleProvider sysSamples = sysReader;

                if (micReader.WaveFormat.SampleRate != sysReader.WaveFormat.SampleRate ||
                    micReader.WaveFormat.Channels != sysReader.WaveFormat.Channels)
                {
                    micSamples = new WdlResamplingSampleProvider(micReader, sysReader.WaveFormat.SampleRate);
                    if (sysReader.WaveFormat.Channels != micReader.WaveFormat.Channels)
                    {
                        micSamples = micReader.WaveFormat.Channels == 1
                            ? new MonoToStereoSampleProvider(micSamples)
                            : new StereoToMonoSampleProvider(micSamples);
                    }
                }

                var mixer = new MixingSampleProvider([micSamples, sysSamples]);
                WaveFileWriter.CreateWaveFile16(outputPath, mixer);
                Log.Warning("Recovered interrupted mixed recording to {Output}", outputPath);
            }

            if (cleanupInputsOnSuccess)
            {
                TryDeleteIfExists(micPath);
                TryDeleteIfExists(sysPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to recover interrupted recording to {Output}", outputPath);
            return false;
        }
    }

    private static string ResolveRecordingsDirectory()
    {
        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
            var storagePath = db.AppSettings
                .AsNoTracking()
                .Where(s => s.Key == "storage_path")
                .Select(s => s.Value)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(storagePath))
                return Path.Combine(storagePath, "recordings");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve custom storage path for recording recovery");
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo");
        return Path.Combine(appData, "recordings");
    }

    private static bool IsRecoverableWave(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        TryRepairWaveHeader(path);
        return new FileInfo(path).Length > 44;
    }

    private static string BuildRecoveredOutputPath(
        string recordingsDir,
        string stamp,
        string? micPath,
        string? sysPath)
    {
        var timestamp = new[] { micPath, sysPath }
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => File.GetLastWriteTime(path!))
            .DefaultIfEmpty(DateTime.Now)
            .Max();

        return Path.Combine(
            recordingsDir,
            $"meeting_recovered_{timestamp:yyyyMMdd_HHmmss}_{stamp}.wav");
    }

    private static void TryDeleteIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete temporary recording file {Path}", path);
        }
    }
}
