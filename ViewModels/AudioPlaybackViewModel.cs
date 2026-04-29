using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxMemo.Services.Platform;

namespace VoxMemo.ViewModels;

public partial class AudioPlaybackViewModel : ViewModelBase
{
    private IAudioPlaybackService? _player;
    private CancellationTokenSource? _playbackTimerCts;
    private bool _isSeeking;

    [ObservableProperty] private bool _isPlaybackActive;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isPlaybackPaused;
    [ObservableProperty] private string _playbackPosition = "00:00 / 00:00";
    [ObservableProperty] private double _playbackCurrentSeconds;
    [ObservableProperty] private double _playbackTotalSeconds;

    public string AudioPath { get; }

    public AudioPlaybackViewModel(string audioPath)
    {
        AudioPath = audioPath;
    }

    partial void OnPlaybackCurrentSecondsChanged(double value)
    {
        if (_isSeeking && _player?.IsInitialized == true)
            _player.CurrentTime = TimeSpan.FromSeconds(value);
    }

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek()
    {
        if (_player?.IsInitialized == true)
            _player.CurrentTime = TimeSpan.FromSeconds(PlaybackCurrentSeconds);
        _isSeeking = false;
    }

    public void SeekTo(double seconds)
    {
        if (_player?.IsInitialized == true && IsPlaybackActive)
        {
            _player.CurrentTime = TimeSpan.FromSeconds(seconds);
            PlaybackCurrentSeconds = seconds;
        }
    }

    [RelayCommand]
    private void PlayAudio()
    {
        if (IsPlaying) return;

        if (IsPlaybackPaused && _player?.IsInitialized == true)
        {
            _player.Play();
            IsPlaying = true;
            IsPlaybackPaused = false;
            return;
        }

        if (string.IsNullOrEmpty(AudioPath) || !File.Exists(AudioPath)) return;

        try
        {
            _player = PlatformServices.CreatePlaybackService();
            _player.PlaybackStopped += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsPlaybackPaused) StopAudioInternal();
                });
            };
            _player.Init(AudioPath);
            PlaybackTotalSeconds = _player.TotalTime.TotalSeconds;
            _player.Play();
            IsPlaying = true;
            IsPlaybackActive = true;
            IsPlaybackPaused = false;
            _playbackTimerCts = new CancellationTokenSource();
            _ = UpdatePlaybackPositionAsync(_playbackTimerCts.Token);
        }
        catch
        {
            StopAudioInternal();
        }
    }

    [RelayCommand]
    private void PauseAudio()
    {
        if (!IsPlaying || _player == null) return;
        _player.Pause();
        IsPlaying = false;
        IsPlaybackPaused = true;
    }

    [RelayCommand]
    private void StopAudio() => StopAudioInternal();

    private void StopAudioInternal()
    {
        _playbackTimerCts?.Cancel();
        _playbackTimerCts?.Dispose();
        _playbackTimerCts = null;
        _player?.Dispose();
        _player = null;
        IsPlaying = false;
        IsPlaybackPaused = false;
        IsPlaybackActive = false;
        PlaybackCurrentSeconds = 0;
        PlaybackTotalSeconds = 0;
        PlaybackPosition = "00:00 / 00:00";
    }

    private async Task UpdatePlaybackPositionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _player?.IsInitialized == true)
        {
            try
            {
                var current = _player.CurrentTime;
                var total = _player.TotalTime;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_isSeeking) PlaybackCurrentSeconds = current.TotalSeconds;
                    PlaybackPosition = $"{current:mm\\:ss} / {total:mm\\:ss}";
                });
                await Task.Delay(250, ct);
            }
            catch { break; }
        }
    }
}
