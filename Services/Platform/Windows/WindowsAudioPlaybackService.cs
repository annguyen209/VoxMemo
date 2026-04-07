using System;
using NAudio.Wave;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsAudioPlaybackService : IAudioPlaybackService
{
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _reader;

    public event EventHandler? PlaybackStopped;
    public bool IsInitialized => _reader != null;

    public TimeSpan CurrentTime
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (_reader != null) try { _reader.CurrentTime = value; } catch { } }
    }

    public TimeSpan TotalTime => _reader?.TotalTime ?? TimeSpan.Zero;

    public void Init(string filePath)
    {
        Dispose();
        _reader = new AudioFileReader(filePath);
        _waveOut = new WaveOutEvent();
        _waveOut.Init(_reader);
        _waveOut.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Play() => _waveOut?.Play();
    public void Pause() => _waveOut?.Pause();

    public void Stop()
    {
        _waveOut?.Stop();
    }

    public void Dispose()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _reader?.Dispose();
        _reader = null;
    }
}
