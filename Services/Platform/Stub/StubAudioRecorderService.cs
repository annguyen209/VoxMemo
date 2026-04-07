using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using VoxMemo.Services.Audio;

namespace VoxMemo.Services.Platform.Stub;

public class StubAudioRecorderService : IAudioRecorder
{
    public event EventHandler<float>? AudioLevelChanged;
    public event EventHandler<string>? RecordingError;

    public bool IsRecording => false;
    public bool IsPaused => false;
    public TimeSpan Elapsed => TimeSpan.Zero;
    public string? LastSnapshotError => "Audio recording not supported on this platform";

    public List<AudioDevice> GetInputDevices() => [];
    public List<AudioDevice> GetLoopbackDevices() => [];

    public Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null)
    {
        Log.Warning("Audio recording is not supported on this platform");
        RecordingError?.Invoke(this, "Audio recording is not supported on this platform");
        return Task.CompletedTask;
    }

    public Task StopRecordingAsync() => Task.CompletedTask;
    public void PauseRecording() { }
    public void ResumeRecording() { }
    public string? CreateSnapshotForTranscription(string tempDir) => null;
}
