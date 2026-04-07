using System;
using System.Diagnostics;
using Serilog;

namespace VoxMemo.Services.Platform.MacOS;

/// <summary>
/// macOS audio playback using afplay.
/// </summary>
public class MacAudioPlaybackService : IAudioPlaybackService
{
    private Process? _process;
    private string? _filePath;
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
        _filePath = filePath;
        _totalTime = new MacAudioConverter().GetDuration(filePath);
        IsInitialized = true;
    }

    public void Play()
    {
        if (!IsInitialized || _filePath == null) return;

        if (_process != null && !_process.HasExited)
        {
            // Resume
            try { Process.Start("kill", $"-CONT {_process.Id}"); } catch { }
        }
        else
        {
            // Start fresh
            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "afplay",
                        Arguments = $"\"{_filePath}\"",
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

                _process.Start();
            }
            catch (Exception ex)
            {
                Log.Warning("afplay failed: {Error}", ex.Message);
                return;
            }
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
