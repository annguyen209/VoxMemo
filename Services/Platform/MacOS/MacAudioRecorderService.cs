using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using VoxMemo.Services.Audio;

namespace VoxMemo.Services.Platform.MacOS;

/// <summary>
/// macOS audio recorder using ffmpeg with avfoundation.
/// System audio loopback requires a virtual audio device (BlackHole, Soundflower).
/// </summary>
public class MacAudioRecorderService : IAudioRecorder, IDisposable
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
            var output = RunCommand("ffmpeg", "-f avfoundation -list_devices true -i \"\" 2>&1");
            return ParseAvfoundationDevices(output, "audio");
        }
        catch
        {
            return [];
        }
    }

    public List<AudioDevice> GetLoopbackDevices()
    {
        // macOS loopback requires virtual audio device (BlackHole, Soundflower)
        // These appear as regular audio inputs in avfoundation
        var devices = GetInputDevices();
        return devices.Where(d =>
            d.Name.Contains("BlackHole", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("Soundflower", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            .Select(d => new AudioDevice(d.Id, $"[Loopback] {d.Name}", true))
            .ToList();
    }

    public Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null)
    {
        try
        {
            string args;
            if (sourceType == AudioSourceType.Both)
            {
                // Try to find a loopback device (BlackHole, Soundflower, etc.)
                var loopbackDevices = GetLoopbackDevices();
                var mic = deviceId ?? "0";

                if (loopbackDevices.Count > 0)
                {
                    var loopback = loopbackDevices[0].Id;
                    // Mix mic + loopback using ffmpeg amix
                    args = $"-f avfoundation -i \":{mic}\" -f avfoundation -i \":{loopback}\" " +
                           $"-filter_complex amix=inputs=2:duration=longest:dropout_transition=0 " +
                           $"-ar 16000 -ac 1 -y \"{outputPath}\"";
                    Log.Information("macOS Both-mode: mic={Mic} loopback={Loopback} output={Path}", mic, loopback, outputPath);
                }
                else
                {
                    // No loopback device found — record mic only
                    args = $"-f avfoundation -i \":{mic}\" -ar 16000 -ac 1 -y \"{outputPath}\"";
                    Log.Warning("No loopback device found on macOS (install BlackHole for system audio). Recording mic only.");
                }
            }
            else
            {
                var input = deviceId ?? "0";
                args = $"-f avfoundation -i \":{input}\" -ar 16000 -ac 1 -y \"{outputPath}\"";
            }

            _recordProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
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

            Log.Information("macOS recording started: source={Source} device={Device} output={Path}", sourceType, deviceId, outputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start macOS recording");
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
                // Send 'q' to ffmpeg stdin for graceful stop
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

        if (new FileInfo(_currentOutputPath).Length < 16000)
        {
            _lastSnapshotError = "Recording file too small";
            return null;
        }

        try
        {
            Directory.CreateDirectory(tempDir);
            var snapshotPath = Path.Combine(tempDir, $"caption_{Guid.NewGuid():N}.wav");

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

    private static List<AudioDevice> ParseAvfoundationDevices(string output, string type)
    {
        var devices = new List<AudioDevice>();
        var inSection = false;

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains($"AVFoundation {type} devices:", StringComparison.OrdinalIgnoreCase))
            { inSection = true; continue; }
            if (line.Contains("AVFoundation", StringComparison.OrdinalIgnoreCase) && inSection)
                break;

            if (inSection && line.Contains(']'))
            {
                var bracketEnd = line.IndexOf(']');
                var bracketStart = line.LastIndexOf('[', bracketEnd);
                if (bracketStart >= 0)
                {
                    var id = line[(bracketStart + 1)..bracketEnd].Trim();
                    var name = line[(bracketEnd + 1)..].Trim();
                    devices.Add(new AudioDevice(id, name, false));
                }
            }
        }

        return devices;
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
        var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }

    public void Dispose()
    {
        StopRecordingAsync().Wait();
        GC.SuppressFinalize(this);
    }
}
