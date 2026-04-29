using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxMemo.Models;
using Serilog;
using VoxMemo.Services.Audio;
using VoxMemo.Services.Database;
using VoxMemo.Services.Platform;
using VoxMemo.Services.Transcription;
using Microsoft.EntityFrameworkCore;

namespace VoxMemo.ViewModels;

public partial class RecordingViewModel : ViewModelBase
{
    private IAudioRecorder? _recorder;
    private DispatcherTimer? _timer;
    private string? _currentAudioPath;
    private DateTime _recordingStartedAt;
    private CancellationTokenSource? _liveCaptionCts;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _elapsedTime = "00:00:00";

    [ObservableProperty]
    private float _audioLevel;

    [ObservableProperty]
    private string _meetingTitle = string.Empty;

    [ObservableProperty]
    private string _selectedLanguage = "en";

    // Audio source: user picks Microphone or System Audio
    [ObservableProperty]
    private string _selectedAudioSource = "Both (Mic + Speaker)";

    [ObservableProperty]
    private ObservableCollection<AudioDeviceItem> _devices = [];

    [ObservableProperty]
    private AudioDeviceItem? _selectedDevice;

    [ObservableProperty]
    private string _statusMessage = Services.Platform.PlatformServices.DependencyWarning ?? "Ready to record";

    [ObservableProperty]
    private string _selectedPlatform = "Other";

    // Live captions
    [ObservableProperty]
    private bool _liveCaptionsEnabled;

    [ObservableProperty]
    private string _liveCaptionText = string.Empty;

    /// <summary>Raised when a recording is saved so Meetings can refresh.</summary>
    public event EventHandler<string>? RecordingSaved;

    public ObservableCollection<string> AudioSources { get; } = ["Microphone", "System Audio", "Both (Mic + Speaker)"];
    public ObservableCollection<string> Platforms { get; } = ["Zoom", "Teams", "Google Meet", "Other"];
    public ObservableCollection<string> Languages { get; } = new(LoadEnabledLanguages());

    private static List<string> LoadEnabledLanguages()
    {
        try
        {
            using var db = new Services.Database.AppDbContext();
            db.Database.EnsureCreated();
            var setting = db.AppSettings.FirstOrDefault(s => s.Key == "enabled_languages");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                return setting.Value.Split(',').ToList();
        }
        catch { }
        return ["en", "vi"];
    }

    public RecordingViewModel()
    {
        // Load default language from settings
        try
        {
            using var db = new Services.Database.AppDbContext();
            var setting = db.AppSettings.FirstOrDefault(s => s.Key == "default_language");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                _selectedLanguage = setting.Value;
        }
        catch { }

        _recorder = Services.Platform.PlatformServices.AudioRecorder;
        _recorder.AudioLevelChanged += (_, level) =>
        {
            Dispatcher.UIThread.Post(() => AudioLevel = level);
        };
        _recorder.RecordingError += (_, err) =>
        {
            Dispatcher.UIThread.Post(() => StatusMessage = $"Recording error: {err}");
        };
        _recorder.RecordingStatus += (_, msg) =>
        {
            Dispatcher.UIThread.Post(() => StatusMessage = msg);
        };
        RefreshDeviceList();

        SettingsViewModel.EnabledLanguagesChanged += (_, codes) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Languages.Clear();
                foreach (var code in codes) Languages.Add(code);
                if (!Languages.Contains(SelectedLanguage) && Languages.Count > 0)
                    SelectedLanguage = Languages[0];
            });
        };
    }

    partial void OnSelectedAudioSourceChanged(string value)
    {
        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        if (_recorder == null) return;

        Devices.Clear();
        // "Both" mode: user picks the microphone; system audio is auto-selected (default loopback)
        var deviceList = SelectedAudioSource == "System Audio"
            ? _recorder.GetLoopbackDevices()
            : _recorder.GetInputDevices();

        Devices.Add(new AudioDeviceItem(null, "Auto (Default)", false));
        foreach (var d in deviceList)
            Devices.Add(new AudioDeviceItem(d.Id, d.Name, d.IsLoopback));

        SelectedDevice = Devices[0];
    }

    private async Task<string> GetStoragePathAsync()
    {
        try
        {
            await using var db = AppDbContextFactory.Create();
            var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "storage_path");
            if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                return setting.Value;
        }
        catch { }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxMemo");
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (_recorder == null)
        {
            StatusMessage = "No audio recorder available";
            return;
        }
        if (SelectedDevice == null && SelectedAudioSource != "System Audio")
        {
            StatusMessage = "No audio device selected";
            return;
        }

        var storagePath = await GetStoragePathAsync();
        var audioDir = Path.Combine(storagePath, "recordings");
        Directory.CreateDirectory(audioDir);

        _currentAudioPath = Path.Combine(audioDir, $"meeting_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        _recordingStartedAt = DateTime.UtcNow;

        var sourceType = SelectedAudioSource switch
        {
            "System Audio" => AudioSourceType.SystemAudio,
            "Both (Mic + Speaker)" => AudioSourceType.Both,
            _ => AudioSourceType.Microphone
        };

        // For "Both" mode, deviceId is the microphone; system audio uses default loopback automatically
        var deviceId = sourceType == AudioSourceType.Both ? SelectedDevice?.Id : SelectedDevice?.Id;
        Log.Information("Starting recording: source={Source} device={Device} path={Path}",
            SelectedAudioSource, SelectedDevice?.Name, _currentAudioPath);
        await _recorder.StartRecordingAsync(_currentAudioPath, sourceType, deviceId);

        IsRecording = true;
        IsPaused = false;
        ElapsedTime = "00:00:00";
        MeetingTitle = string.Empty;
        StatusMessage = sourceType == AudioSourceType.Both
            ? $"Recording mic + speaker (mixed)..."
            : $"Recording from {SelectedAudioSource}...";
        LiveCaptionText = string.Empty;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            if (_recorder != null)
                ElapsedTime = _recorder.Elapsed.ToString(@"hh\:mm\:ss");
        };
        _timer.Start();

        if (LiveCaptionsEnabled)
            StartLiveCaptions();
    }

    private void StartLiveCaptions()
    {
        _liveCaptionCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            var ct = _liveCaptionCts.Token;
            var transcriber = new WhisperTranscriptionService();

            // Always use "tiny" for live captions — it's ~10x faster than "small"
            const string captionModel = "tiny";
            var models = await transcriber.GetAvailableModelsAsync();

            if (!models.Contains(captionModel))
            {
                Dispatcher.UIThread.Post(() =>
                    LiveCaptionText = "Downloading tiny model for live captions...");
                Log.Information("Auto-downloading tiny model for live captions");

                try
                {
                    await transcriber.DownloadModelAsync(captionModel, null, ct);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to download tiny model");
                    Dispatcher.UIThread.Post(() =>
                        LiveCaptionText = $"Failed to download tiny model: {ex.Message}");
                    return;
                }
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "VoxMemo_captions");

            Dispatcher.UIThread.Post(() =>
                LiveCaptionText = "Starting live captions...");

            // Short initial wait for audio to accumulate
            try { await Task.Delay(3000, ct); }
            catch (OperationCanceledException) { return; }

            while (!ct.IsCancellationRequested && IsRecording)
            {
                try
                {
                    var snapshotPath = _recorder?.CreateSnapshotForTranscription(tempDir);

                    if (snapshotPath != null)
                    {
                        try
                        {
                            var result = await transcriber.TranscribeAsync(
                                snapshotPath, SelectedLanguage, captionModel, ct);

                            Dispatcher.UIThread.Post(() =>
                            {
                                if (result.Segments.Count > 0)
                                {
                                    var count = Math.Min(5, result.Segments.Count);
                                    var lastSegments = result.Segments.GetRange(
                                        result.Segments.Count - count, count);
                                    LiveCaptionText = string.Join(" ",
                                        lastSegments.ConvertAll(s => s.Text));
                                }
                                else
                                {
                                    LiveCaptionText = "(listening...)";
                                }
                            });
                        }
                        finally
                        {
                            try { File.Delete(snapshotPath); } catch { }
                        }
                    }
                    else
                    {
                        var error = _recorder?.LastSnapshotError ?? "unknown";
                        Dispatcher.UIThread.Post(() =>
                            LiveCaptionText = $"(waiting: {error})");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        LiveCaptionText = $"Caption error: {ex.Message}");
                }

                // Short delay — transcribe again quickly
                try { await Task.Delay(3000, ct); }
                catch (OperationCanceledException) { break; }
            }

            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        });
    }

    [RelayCommand]
    private void PauseRecording()
    {
        if (_recorder == null || !IsRecording) return;

        if (IsPaused)
        {
            _recorder.ResumeRecording();
            IsPaused = false;
            StatusMessage = "Recording...";
            _timer?.Start();
        }
        else
        {
            _recorder.PauseRecording();
            IsPaused = true;
            StatusMessage = "Paused";
            _timer?.Stop();
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (_recorder == null || !IsRecording) return;

        _liveCaptionCts?.Cancel();
        _liveCaptionCts?.Dispose();
        _liveCaptionCts = null;

        _timer?.Stop();
        var elapsed = _recorder.Elapsed;
        Log.Information("Stopping recording after {Elapsed}", elapsed);
        await _recorder.StopRecordingAsync();

        IsRecording = false;
        IsPaused = false;

        // Check if recording has actual data
        if (_currentAudioPath == null || !File.Exists(_currentAudioPath))
        {
            Log.Warning("Recording failed - no file created at {Path}", _currentAudioPath);
            StatusMessage = "Recording failed - no file created";
            return;
        }

        var fileSize = new FileInfo(_currentAudioPath).Length;
        Log.Information("Recording file size: {Size} bytes at {Path}", fileSize, _currentAudioPath);
        if (fileSize < 1000)
        {
            Log.Warning("Recording is empty ({Size} bytes)", fileSize);
            StatusMessage = "Recording is empty - no audio was captured. Check your device selection.";
            try { File.Delete(_currentAudioPath); } catch { }
            return;
        }

        // Convert WAV to Whisper-compatible format (16kHz 16-bit mono)
        try
        {
            StatusMessage = "Converting audio format...";
            await Task.Run(() => Services.Platform.PlatformServices.AudioConverter.ConvertInPlace(_currentAudioPath));
            Log.Information("Audio converted successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Audio conversion failed");
            StatusMessage = $"Warning: conversion failed ({ex.Message})";
        }

        // Save meeting to database immediately with default title
        try
        {
            var title = string.IsNullOrWhiteSpace(MeetingTitle)
                ? $"Meeting {_recordingStartedAt.ToLocalTime():MMM dd, yyyy HH:mm}"
                : MeetingTitle;

            var meeting = new Meeting
            {
                Title = title,
                Platform = SelectedPlatform,
                StartedAt = _recordingStartedAt,
                EndedAt = DateTime.UtcNow,
                AudioPath = _currentAudioPath,
                DurationMs = (long)elapsed.TotalMilliseconds,
                Language = SelectedLanguage,
            };

            await using var db = AppDbContextFactory.Create();
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();

            Log.Information("Meeting saved: {Title} id={Id}", title, meeting.Id);
            StatusMessage = $"Saved: {title}";
            MainWindowViewModel.ShowTrayNotification("VoxMemo", $"Recording saved: {title} ({elapsed:mm\\:ss})");
            MeetingTitle = string.Empty;
            LiveCaptionText = string.Empty;

            RecordingSaved?.Invoke(this, meeting.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save meeting");
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshDevices() => RefreshDeviceList();
}

public record AudioDeviceItem(string? Id, string Name, bool IsLoopback)
{
    public override string ToString() => Name;
}
