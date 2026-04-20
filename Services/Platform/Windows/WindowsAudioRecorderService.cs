using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

using VoxMemo.Services.Audio;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsAudioRecorderService : IAudioRecorder, IDisposable
{
    // Single-source mode
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private WaveFormat? _captureFormat;

    // Both-mode (mic + system audio)
    private bool _isMixMode;
    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _sysCapture;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _sysWriter;
    private string? _mixFinalPath;
    private string? _micTempPath;
    private string? _sysTempPath;

    private readonly Stopwatch _stopwatch = new();
    private bool _isPaused;

    // Buffer recent audio for live caption snapshots (from primary/sys capture)
    private readonly object _bufferLock = new();
    private readonly List<byte> _recentBuffer = [];
    private WaveFormat? _bufferFormat;
    private const int MaxBufferSeconds = 10;

    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<string>? RecordingError;

    public bool IsRecording => (_capture != null || _sysCapture != null) && _stopwatch.IsRunning;
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

            if (sourceType == AudioSourceType.Both)
            {
                _isMixMode = true;
                _mixFinalPath = outputPath;

                var tempDir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
                var stamp = Guid.NewGuid().ToString("N")[..8];
                _micTempPath = Path.Combine(tempDir, $"vox_mic_{stamp}.wav");
                _sysTempPath = Path.Combine(tempDir, $"vox_sys_{stamp}.wav");

                // Mic capture
                MMDevice micDevice = deviceId != null
                    ? enumerator.GetDevice(deviceId)
                    : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _micCapture = new WasapiCapture(micDevice);
                _micWriter = new WaveFileWriter(_micTempPath, _micCapture.WaveFormat);

                // System audio capture (default render device loopback)
                MMDevice sysDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _sysCapture = new WasapiLoopbackCapture(sysDevice);
                var sysFormat = _sysCapture.WaveFormat;
                _sysWriter = new WaveFileWriter(_sysTempPath, sysFormat);

                _bufferFormat = sysFormat;
                lock (_bufferLock) _recentBuffer.Clear();

                _micCapture.DataAvailable += OnMicDataAvailable;
                _sysCapture.DataAvailable += OnSysDataAvailable;

                _sysCapture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null) RecordingError?.Invoke(this, e.Exception.Message);
                };

                _micCapture.StartRecording();
                _sysCapture.StartRecording();

                Log.Information("Both-mode recording started: mic={Mic} output={Path}", deviceId, outputPath);
            }
            else
            {
                _isMixMode = false;

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
                _bufferFormat = _captureFormat;
                Log.Information("Recording started: {Source} device={DeviceId} format={Format} output={Path}",
                    sourceType, deviceId, _captureFormat, outputPath);
                _writer = new WaveFileWriter(outputPath, _captureFormat);

                lock (_bufferLock) _recentBuffer.Clear();

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, e) =>
                {
                    if (e.Exception != null) RecordingError?.Invoke(this, e.Exception.Message);
                };
                _capture.StartRecording();
            }

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
            UpdateBuffer(e.Buffer, e.BytesRecorded);
            UpdateAudioLevel(e.Buffer, e.BytesRecorded, _captureFormat);
        }
        catch { }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || _micWriter == null || e.BytesRecorded == 0) return;
        try { _micWriter.Write(e.Buffer, 0, e.BytesRecorded); }
        catch { }
    }

    private void OnSysDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || _sysWriter == null || e.BytesRecorded == 0) return;
        try
        {
            _sysWriter.Write(e.Buffer, 0, e.BytesRecorded);
            UpdateBuffer(e.Buffer, e.BytesRecorded);
            UpdateAudioLevel(e.Buffer, e.BytesRecorded, _bufferFormat);
        }
        catch { }
    }

    private void UpdateBuffer(byte[] buffer, int bytesRecorded)
    {
        lock (_bufferLock)
        {
            _recentBuffer.AddRange(buffer.AsSpan(0, bytesRecorded).ToArray());
            if (_bufferFormat != null)
            {
                int maxBytes = _bufferFormat.AverageBytesPerSecond * MaxBufferSeconds;
                if (_recentBuffer.Count > maxBytes)
                    _recentBuffer.RemoveRange(0, _recentBuffer.Count - maxBytes);
            }
        }
    }

    private void UpdateAudioLevel(byte[] buffer, int bytesRecorded, WaveFormat? format)
    {
        float max = 0;
        if (format?.BitsPerSample == 32 && format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (int i = 0; i < bytesRecorded - 3; i += 4)
            {
                float sample = Math.Abs(BitConverter.ToSingle(buffer, i));
                if (sample > max) max = sample;
            }
        }
        else if (format?.BitsPerSample == 16)
        {
            for (int i = 0; i < bytesRecorded - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float sampleF = Math.Abs(sample / 32768f);
                if (sampleF > max) max = sampleF;
            }
        }
        AudioLevelChanged?.Invoke(this, Math.Min(max, 1.0f));
    }

    public string? LastSnapshotError { get; private set; }

    public string? CreateSnapshotForTranscription(string tempDir)
    {
        var format = _bufferFormat;
        if (format == null)
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
            if (_recentBuffer.Count < format.AverageBytesPerSecond)
            {
                LastSnapshotError = $"Buffer too small ({_recentBuffer.Count} bytes, need {format.AverageBytesPerSecond})";
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

            using (var writer = new WaveFileWriter(rawPath, format))
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

            try
            {
                new WindowsAudioConverter().ConvertToWhisperFormat(rawPath, convertedPath);
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

    public async Task StopRecordingAsync()
    {
        _stopwatch.Stop();

        if (_isMixMode)
        {
            // Stop both captures
            if (_micCapture != null)
            {
                _micCapture.DataAvailable -= OnMicDataAvailable;
                _micCapture.StopRecording();
            }
            if (_sysCapture != null)
            {
                _sysCapture.DataAvailable -= OnSysDataAvailable;
                _sysCapture.StopRecording();
            }

            _micWriter?.Dispose();
            _sysWriter?.Dispose();
            _micCapture?.Dispose();
            _sysCapture?.Dispose();
            _micWriter = null;
            _sysWriter = null;
            _micCapture = null;
            _sysCapture = null;

            // Mix mic + system audio into final output
            if (_mixFinalPath != null && _micTempPath != null && _sysTempPath != null)
            {
                await Task.Run(() =>
                    WindowsRecordingRecoveryService.TryCreateMixedRecording(
                        _micTempPath,
                        _sysTempPath,
                        _mixFinalPath,
                        cleanupInputsOnSuccess: true));
            }

            _mixFinalPath = null;
            _micTempPath = null;
            _sysTempPath = null;
            _isMixMode = false;
        }
        else
        {
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.StopRecording();
            }

            _writer?.Dispose();
            _capture?.Dispose();
            _writer = null;
            _capture = null;
        }

        _captureFormat = null;
        _bufferFormat = null;
        _isPaused = false;
        lock (_bufferLock) _recentBuffer.Clear();
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
        _micWriter?.Dispose();
        _sysWriter?.Dispose();
        _micCapture?.Dispose();
        _sysCapture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
