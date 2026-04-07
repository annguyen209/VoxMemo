using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using VoxMemo.Services.Audio;

namespace VoxMemo.Services.Platform.Linux;

/// <summary>
/// Linux audio recorder using PulseAudio (parecord) or PipeWire (pw-record).
/// System audio capture uses monitor sources.
/// </summary>
public class LinuxAudioRecorderService : IAudioRecorder, IDisposable
{
    private Process? _recordProcess;
    private readonly Stopwatch _stopwatch = new();
    private bool _isPaused;
    private string? _lastSnapshotError;
    private string? _currentOutputPath;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<string>? RecordingError;

    public bool IsRecording => _recordProcess != null && !_recordProcess.HasExited;
    public bool IsPaused => _isPaused;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public string? LastSnapshotError => _lastSnapshotError;

    public List<AudioDevice> GetInputDevices()
    {
        try
        {
            var output = RunCommand("pactl", "list short sources");
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                .Select(line =>
                {
                    var parts = line.Split('\t');
                    var name = parts.Length > 1 ? parts[1] : parts[0];
                    return new AudioDevice(name, name, false);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to list input devices: {Error}", ex.Message);
            return [];
        }
    }

    public List<AudioDevice> GetLoopbackDevices()
    {
        try
        {
            var output = RunCommand("pactl", "list short sources");
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                .Select(line =>
                {
                    var parts = line.Split('\t');
                    var name = parts.Length > 1 ? parts[1] : parts[0];
                    return new AudioDevice(name, $"[Loopback] {name}", true);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to list loopback devices: {Error}", ex.Message);
            return [];
        }
    }

    public Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null)
    {
        try
        {
            // Use parecord (PulseAudio) to record as WAV
            var args = $"--format=s16le --rate=16000 --channels=1 --file-format=wav";
            if (!string.IsNullOrEmpty(deviceId))
                args += $" --device={deviceId}";
            args += $" {outputPath}";

            _recordProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "parecord",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            _recordProcess.Start();
            _stopwatch.Restart();
            _isPaused = false;
            _currentOutputPath = outputPath;

            Log.Information("Linux recording started: device={Device} output={Path}", deviceId, outputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Linux recording");
            RecordingError?.Invoke(this, ex.Message);
        }

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        _stopwatch.Stop();

        if (_recordProcess != null && !_recordProcess.HasExited)
        {
            try
            {
                _recordProcess.Kill(true);
                _recordProcess.WaitForExit(3000);
            }
            catch { }
        }

        _recordProcess?.Dispose();
        _recordProcess = null;
        _isPaused = false;

        return Task.CompletedTask;
    }

    public void PauseRecording()
    {
        // parecord doesn't support pause natively; send SIGSTOP
        if (_recordProcess != null && !_recordProcess.HasExited)
        {
            try { RunCommand("kill", $"-STOP {_recordProcess.Id}"); } catch { }
            _isPaused = true;
            _stopwatch.Stop();
        }
    }

    public void ResumeRecording()
    {
        if (_recordProcess != null && !_recordProcess.HasExited)
        {
            try { RunCommand("kill", $"-CONT {_recordProcess.Id}"); } catch { }
            _isPaused = false;
            _stopwatch.Start();
        }
    }

    public string? CreateSnapshotForTranscription(string tempDir)
    {
        if (string.IsNullOrEmpty(_currentOutputPath) || !File.Exists(_currentOutputPath))
        {
            _lastSnapshotError = "No recording file available";
            return null;
        }

        if (new FileInfo(_currentOutputPath).Length < 16000) // ~0.5s of 16kHz audio
        {
            _lastSnapshotError = "Recording file too small";
            return null;
        }

        try
        {
            Directory.CreateDirectory(tempDir);
            var snapshotPath = Path.Combine(tempDir, $"caption_{Guid.NewGuid():N}.wav");

            // Extract last 10 seconds from the in-progress recording file
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-sseof -10 -i \"{_currentOutputPath}\" -ar 16000 -ac 1 -sample_fmt s16 -y \"{snapshotPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) { _lastSnapshotError = "Failed to start ffmpeg"; return null; }
            proc.WaitForExit(10000);

            if (File.Exists(snapshotPath) && new FileInfo(snapshotPath).Length > 1000)
            {
                _lastSnapshotError = null;
                return snapshotPath;
            }

            _lastSnapshotError = $"ffmpeg snapshot failed (exit {proc.ExitCode})";
            return null;
        }
        catch (Exception ex)
        {
            _lastSnapshotError = $"Snapshot failed: {ex.Message}";
            return null;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null) return string.Empty;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }

    public void Dispose()
    {
        StopRecordingAsync().Wait();
        GC.SuppressFinalize(this);
    }
}
