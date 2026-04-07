using System;
using Serilog;

namespace VoxMemo.Services.Platform.Stub;

public class StubAudioPlaybackService : IAudioPlaybackService
{
    public event EventHandler? PlaybackStopped;
    public bool IsInitialized => false;
    public TimeSpan CurrentTime { get; set; }
    public TimeSpan TotalTime => TimeSpan.Zero;

    public void Init(string filePath)
    {
        Log.Warning("Audio playback not supported on this platform");
    }

    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Dispose() { }
}
