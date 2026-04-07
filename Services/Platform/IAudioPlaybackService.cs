using System;

namespace VoxMemo.Services.Platform;

public interface IAudioPlaybackService : IDisposable
{
    event EventHandler? PlaybackStopped;
    void Init(string filePath);
    void Play();
    void Pause();
    void Stop();
    TimeSpan CurrentTime { get; set; }
    TimeSpan TotalTime { get; }
    bool IsInitialized { get; }
}
