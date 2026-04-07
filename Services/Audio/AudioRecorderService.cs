using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace VoxMemo.Services.Audio;

public class AudioRecorderService : IAudioRecorder, IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WaveFormat? _captureFormat;
    private readonly Stopwatch _stopwatch = new();
    private bool _isPaused;

    // Buffer recent audio for live caption snapshots
    private readonly object _bufferLock = new();
    private readonly List<byte> _recentBuffer = [];
    private const int MaxBufferSeconds = 10;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<string>? RecordingError;

    public bool IsRecording => _capture != null && _stopwatch.IsRunning;
    public bool IsPaused => _isPaused;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public List<AudioDevice> GetInputDevices()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new AudioDevice(d.ID, d.FriendlyName, false))
                .ToList();
        }
        catch { return []; }
    }

    public List<AudioDevice> GetLoopbackDevices()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new AudioDevice(d.ID, $"[Loopback] {d.FriendlyName}", true))
                .ToList();
        }
        catch { return []; }
    }

    public Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();

            if (sourceType == AudioSourceType.SystemAudio)
            {
                MMDevice device = deviceId != null
                    ? enumerator.GetDevice(deviceId)
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _capture = new WasapiLoopbackCapture(device);
            }
            else
            {
                MMDevice device = deviceId != null
                    ? enumerator.GetDevice(deviceId)
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _capture = new WasapiCapture(device);
            }

            _captureFormat = _capture.WaveFormat;
            Log.Information("Recording started: {Source} device={DeviceId} format={Format} output={Path}",
                sourceType, deviceId, _captureFormat, outputPath);
            _writer = new WaveFileWriter(outputPath, _captureFormat);

            lock (_bufferLock) _recentBuffer.Clear();

            _capture.DataAvailable += OnDataAvailable;

            _capture.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null)
                    RecordingError?.Invoke(this, e.Exception.Message);
            };

            _capture.StartRecording();
            _stopwatch.Restart();
            _isPaused = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start recording");
            RecordingError?.Invoke(this, ex.Message);
        }

        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || _writer == null || e.BytesRecorded == 0) return;

        try
        {
            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            // Keep recent audio in memory for live captions
            lock (_bufferLock)
            {
                _recentBuffer.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());

                // Trim to max buffer size
                if (_captureFormat != null)
                {
                    int maxBytes = _captureFormat.AverageBytesPerSecond * MaxBufferSeconds;
                    if (_recentBuffer.Count > maxBytes)
                    {
                        _recentBuffer.RemoveRange(0, _recentBuffer.Count - maxBytes);
                    }
                }
            }

            // Calculate audio level for visualization
            float max = 0;
            if (_captureFormat?.BitsPerSample == 32 && _captureFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < e.BytesRecorded - 3; i += 4)
                {
                    float sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                    if (sample > max) max = sample;
                }
            }
            else if (_captureFormat?.BitsPerSample == 16)
            {
                for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i);
                    float sampleF = Math.Abs(sample / 32768f);
                    if (sampleF > max) max = sampleF;
                }
            }

            AudioLevelChanged?.Invoke(this, Math.Min(max, 1.0f));
        }
        catch { }
    }

    /// <summary>
    /// Writes the recent audio buffer to a temporary WAV file converted to Whisper format.
    /// Returns the path, or null if no data available.
    /// LastSnapshotError contains diagnostic info if null is returned.
    /// </summary>
    public string? LastSnapshotError { get; private set; }

    public string? CreateSnapshotForTranscription(string tempDir)
    {
        if (_captureFormat == null)
        {
            LastSnapshotError = "No capture format";
            return null;
        }

        byte[] data;
        lock (_bufferLock)
        {
            if (_recentBuffer.Count == 0)
            {
                LastSnapshotError = "Buffer is empty — no audio data received";
                return null;
            }
            if (_recentBuffer.Count < _captureFormat.AverageBytesPerSecond)
            {
                LastSnapshotError = $"Buffer too small ({_recentBuffer.Count} bytes, need {_captureFormat.AverageBytesPerSecond})";
                return null;
            }
            data = _recentBuffer.ToArray();
        }

        string? rawPath = null;
        try
        {
            Directory.CreateDirectory(tempDir);
            rawPath = Path.Combine(tempDir, $"caption_raw_{Guid.NewGuid():N}.wav");
            var convertedPath = Path.Combine(tempDir, $"caption_{Guid.NewGuid():N}.wav");

            // Write raw WAV in capture format
            using (var writer = new WaveFileWriter(rawPath, _captureFormat))
            {
                writer.Write(data, 0, data.Length);
            }

            var rawInfo = new FileInfo(rawPath);
            if (rawInfo.Length < 1000)
            {
                LastSnapshotError = $"Raw WAV too small ({rawInfo.Length} bytes)";
                try { File.Delete(rawPath); } catch { }
                return null;
            }

            // Try conversion, fall back to raw file if it fails
            try
            {
                AudioConverter.ConvertToWhisperFormat(rawPath, convertedPath);
                try { File.Delete(rawPath); } catch { }

                var convertedInfo = new FileInfo(convertedPath);
                if (convertedInfo.Length < 1000)
                {
                    LastSnapshotError = $"Converted WAV too small ({convertedInfo.Length} bytes)";
                    try { File.Delete(convertedPath); } catch { }
                    return null;
                }

                LastSnapshotError = null;
                return convertedPath;
            }
            catch (Exception convEx)
            {
                // Conversion failed — try feeding raw file directly
                // Whisper.net can often handle various formats
                Log.Warning(convEx, "Audio conversion failed for live caption, falling back to raw audio");
                try { File.Delete(convertedPath); } catch { }
                LastSnapshotError = $"Conversion failed ({convEx.Message}), using raw audio";
                return rawPath;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Snapshot creation failed");
            LastSnapshotError = $"Snapshot failed: {ex.Message}";
            if (rawPath != null) try { File.Delete(rawPath); } catch { }
            return null;
        }
    }

    public Task StopRecordingAsync()
    {
        _stopwatch.Stop();

        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
        }

        _writer?.Dispose();
        _capture?.Dispose();
        _writer = null;
        _capture = null;
        _isPaused = false;

        lock (_bufferLock) _recentBuffer.Clear();

        return Task.CompletedTask;
    }

    public void PauseRecording()
    {
        _isPaused = true;
        _stopwatch.Stop();
    }

    public void ResumeRecording()
    {
        _isPaused = false;
        _stopwatch.Start();
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _capture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
