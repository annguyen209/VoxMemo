using System;
using System.Diagnostics;
using System.Threading;
using Serilog;

namespace VoxMemo.Services.Platform.Linux;

/// <summary>
/// Linux audio playback using ffplay (from ffmpeg suite).
/// </summary>
public class LinuxAudioPlaybackService : IAudioPlaybackService
{
    private Process? _process;
    private TimeSpan _totalTime;
    private DateTime _playStartedAt;
    private TimeSpan _playOffset;
    private bool _isPlaying;

    public event EventHandler? PlaybackStopped;
    public bool IsInitialized { get; private set; }

    public TimeSpan TotalTime => _totalTime;

    public TimeSpan CurrentTime
    {
        get => _isPlaying
            ? _playOffset + (DateTime.Now - _playStartedAt)
            : _playOffset;
        set => _playOffset = value;
    }

    public void Init(string filePath)
    {
        Dispose();

        // Get duration via ffprobe
        _totalTime = new LinuxAudioConverter().GetDuration(filePath);

        // Start ffplay in paused state
        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffplay",
                    Arguments = $"-nodisp -autoexit -loglevel quiet \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            _process.Exited += (_, _) =>
            {
                _isPlaying = false;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            };

            IsInitialized = true;
        }
        catch (Exception ex)
        {
            Log.Warning("ffplay init failed: {Error}", ex.Message);
        }
    }

    public void Play()
    {
        if (!IsInitialized) return;

        if (_process != null && !_process.HasExited)
        {
            // Resume from SIGSTOP
            try { Process.Start("kill", $"-CONT {_process.Id}"); } catch { }
        }
        else if (_process != null)
        {
            // Start fresh
            try { _process.Start(); } catch { }
        }

        _playStartedAt = DateTime.Now;
        _isPlaying = true;
    }

    public void Pause()
    {
        if (_process != null && !_process.HasExited)
        {
            _playOffset = CurrentTime;
            _isPlaying = false;
            try { Process.Start("kill", $"-STOP {_process.Id}"); } catch { }
        }
    }

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(true); } catch { }
        }
        _isPlaying = false;
        _playOffset = TimeSpan.Zero;
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
        IsInitialized = false;
    }
}
