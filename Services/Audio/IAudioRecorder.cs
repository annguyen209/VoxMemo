using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoxMemo.Services.Audio;

public enum AudioSourceType
{
    Microphone,
    SystemAudio,
    Both,           // Mix microphone + system audio (for meeting capture)
}

public record AudioDevice(string Id, string Name, bool IsLoopback);

public interface IAudioRecorder
{
    event EventHandler<float>? AudioLevelChanged;
    event EventHandler<string>? RecordingError;

    /// <summary>Raised to report progress during long operations (mixing, converting).</summary>
    event EventHandler<string>? RecordingStatus;

    List<AudioDevice> GetInputDevices();
    List<AudioDevice> GetLoopbackDevices();
    Task StartRecordingAsync(string outputPath, AudioSourceType sourceType, string? deviceId = null);
    Task StopRecordingAsync();
    void PauseRecording();
    void ResumeRecording();
    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }

    /// <summary>Creates a snapshot of recent audio for live transcription. Returns file path or null.</summary>
    string? CreateSnapshotForTranscription(string tempDir);

    /// <summary>Diagnostic info when CreateSnapshotForTranscription returns null.</summary>
    string? LastSnapshotError { get; }
}
