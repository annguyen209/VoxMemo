using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxMemo.Services.Audio;

public enum AudioSourceType
{
    Microphone,
    SystemAudio,
}

public record AudioDevice(string Id, string Name, bool IsLoopback);

public interface IAudioRecorder
{
    event EventHandler<float>? AudioLevelChanged;
    event EventHandler<string>? RecordingError;

    List<AudioDevice> GetInputDevices();
    List<AudioDevice> GetLoopbackDevices();
    Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null);
    Task StopRecordingAsync();
    void PauseRecording();
    void ResumeRecording();
    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }
}
