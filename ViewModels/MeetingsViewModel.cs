using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using VoxMemo.Models;
using VoxMemo.Services.AI;
using VoxMemo.Services.Database;
using VoxMemo.Services.Platform;
using VoxMemo.Services.Transcription;
using Microsoft.EntityFrameworkCore;

namespace VoxMemo.ViewModels;

public partial class MeetingsViewModel : ViewModelBase
{
    private List<MeetingItemViewModel> _allMeetings = [];

    [ObservableProperty]
    private ObservableCollection<MeetingItemViewModel> _meetings = [];

    [ObservableProperty]
    private MeetingItemViewModel? _selectedMeeting;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _meetingCount = "0 meetings";

    public MeetingsViewModel()
    {
        _ = LoadMeetingsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterMeetings();
    }

    private void FilterMeetings()
    {
        var query = SearchText?.Trim() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? _allMeetings
            : _allMeetings.Where(m =>
                m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Platform.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.StartedAt.ToString("MMM dd, yyyy").Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Meetings.Clear();
        foreach (var m in filtered)
            Meetings.Add(m);

        MeetingCount = _allMeetings.Count == filtered.Count
            ? $"{_allMeetings.Count} meetings"
            : $"{filtered.Count} of {_allMeetings.Count} meetings";
    }

    [RelayCommand]
    private async Task LoadMeetingsAsync()
    {
        IsLoading = true;
        try
        {
            await using var db = new AppDbContext();
            await db.Database.EnsureCreatedAsync();

            var meetings = await db.Meetings
                .Include(m => m.Transcripts)
                .Include(m => m.Summaries)
                .OrderByDescending(m => m.StartedAt)
                .ToListAsync();

            _allMeetings = meetings.Select(m => new MeetingItemViewModel(m)).ToList();
            FilterMeetings();
        }
        catch { }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task<bool> ConfirmDialogAsync(
        string title, string message, string? detail, string confirmText, string confirmColor)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return true;

        var confirmed = false;
        var dialog = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 440,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
            ExtendClientAreaToDecorationsHint = false,
        };

        var root = new Avalonia.Controls.Border
        {
            Padding = new Avalonia.Thickness(28, 24),
            Child = new Avalonia.Controls.StackPanel
            {
                Spacing = 20,
            }
        };

        var contentPanel = (Avalonia.Controls.StackPanel)root.Child;

        // Message
        contentPanel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = Avalonia.Media.Brush.Parse("#bac2de"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        // Detail (optional)
        if (!string.IsNullOrEmpty(detail))
        {
            contentPanel.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = detail,
                FontSize = 12,
                Foreground = Avalonia.Media.Brush.Parse("#7f849c"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }

        // Buttons
        var buttonPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        var confirmBtn = new Avalonia.Controls.Button
        {
            Content = confirmText,
            Background = Avalonia.Media.Brush.Parse(confirmColor),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        confirmBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(confirmBtn);
        contentPanel.Children.Add(buttonPanel);

        dialog.Content = root;
        await dialog.ShowDialog(desktop.MainWindow);
        return confirmed;
    }

    [RelayCommand]
    private async Task CreateFromTextAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        // Show dialog with title + text area
        var confirmed = false;
        string title = "";
        string transcript = "";
        string language = "en";

        var dialog = new Avalonia.Controls.Window
        {
            Title = "New Meeting from Text",
            Width = 550,
            Height = 480,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
        };

        // Read default language
        try
        {
            await using var db2 = new AppDbContext();
            var langSetting = await db2.AppSettings.FirstOrDefaultAsync(s => s.Key == "default_language");
            if (langSetting != null) language = langSetting.Value;
        }
        catch { }

        // Load enabled languages
        List<string> languages = ["en", "vi"];
        try
        {
            await using var db3 = new AppDbContext();
            var langListSetting = await db3.AppSettings.FirstOrDefaultAsync(s => s.Key == "enabled_languages");
            if (langListSetting != null && !string.IsNullOrEmpty(langListSetting.Value))
                languages = langListSetting.Value.Split(',').ToList();
        }
        catch { }

        var content = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(24, 20),
            Spacing = 14,
        };

        // Title
        content.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Title", FontSize = 12,
            Foreground = Avalonia.Media.Brush.Parse("#bac2de"),
        });
        var titleBox = new Avalonia.Controls.TextBox
        {
            Watermark = "Meeting title...",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12, 8),
        };
        content.Children.Add(titleBox);

        // Language
        content.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Language", FontSize = 12,
            Foreground = Avalonia.Media.Brush.Parse("#bac2de"),
        });
        var langCombo = new Avalonia.Controls.ComboBox
        {
            ItemsSource = languages,
            SelectedItem = language,
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 6),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            MinWidth = 100,
        };
        content.Children.Add(langCombo);

        // Transcript
        content.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Transcript", FontSize = 12,
            Foreground = Avalonia.Media.Brush.Parse("#bac2de"),
        });
        var textBox = new Avalonia.Controls.TextBox
        {
            Watermark = "Paste or type the transcript here...",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 180,
            MaxHeight = 250,
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12, 8),
        };
        content.Children.Add(textBox);

        // Buttons
        var buttons = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        var createBtn = new Avalonia.Controls.Button
        {
            Content = "Create Meeting",
            Background = Avalonia.Media.Brush.Parse("#89b4fa"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        createBtn.Click += (_, _) =>
        {
            title = titleBox.Text ?? "";
            transcript = textBox.Text ?? "";
            language = langCombo.SelectedItem?.ToString() ?? "en";
            confirmed = true;
            dialog.Close();
        };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(createBtn);
        content.Children.Add(buttons);
        dialog.Content = content;

        await dialog.ShowDialog(desktop.MainWindow);
        if (!confirmed || string.IsNullOrWhiteSpace(transcript)) return;

        if (string.IsNullOrWhiteSpace(title))
            title = $"Text Import {DateTime.Now:MMM dd, yyyy HH:mm}";

        // Save meeting + transcript to DB
        var meeting = new Meeting
        {
            Title = title,
            Platform = "Text Import",
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            Language = language,
        };

        await using var dbSave = new AppDbContext();
        dbSave.Meetings.Add(meeting);

        dbSave.Transcripts.Add(new Transcript
        {
            MeetingId = meeting.Id,
            Engine = "manual",
            Language = language,
            FullText = transcript,
        });

        await dbSave.SaveChangesAsync();

        await LoadMeetingsAsync();
        SelectedMeeting = Meetings.FirstOrDefault(m => m.Id == meeting.Id);
    }

    [RelayCommand]
    private async Task CreateMeetingAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select audio file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Audio files")
                    {
                        Patterns = ["*.wav", "*.mp3", "*.m4a", "*.ogg", "*.flac", "*.wma", "*.aac"]
                    }
                ]
            });

        if (files.Count == 0) return;

        var sourcePath = files[0].Path.LocalPath;
        if (!File.Exists(sourcePath)) return;

        // Copy audio to app storage
        string storagePath;
        try
        {
            await using var settingsDb = new AppDbContext();
            var setting = await settingsDb.AppSettings.FirstOrDefaultAsync(s => s.Key == "storage_path");
            storagePath = setting?.Value ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxMemo");
        }
        catch
        {
            storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxMemo");
        }

        var audioDir = Path.Combine(storagePath, "recordings");
        Directory.CreateDirectory(audioDir);

        var ext = Path.GetExtension(sourcePath).ToLower();
        var destPath = Path.Combine(audioDir, $"imported_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        // Get duration from source, then convert to Whisper-compatible WAV
        long durationMs = 0;
        try
        {
            durationMs = (long)PlatformServices.AudioConverter.GetDuration(sourcePath).TotalMilliseconds;
        }
        catch { }

        try
        {
            if (ext == ".wav")
            {
                File.Copy(sourcePath, destPath, true);
                // Convert in-place to Whisper format
                await Task.Run(() => PlatformServices.AudioConverter.ConvertInPlace(destPath));
            }
            else
            {
                // Convert any format (mp3, m4a, ogg, flac, etc.) to Whisper WAV
                await Task.Run(() => PlatformServices.AudioConverter.ConvertToWhisperFormat(sourcePath, destPath));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to convert imported audio {Source}", sourcePath);
            // Fallback: just copy the file as-is
            File.Copy(sourcePath, destPath, true);
        }

        // Read default language from settings
        string language = "en";
        try
        {
            await using var db2 = new AppDbContext();
            var langSetting = await db2.AppSettings.FirstOrDefaultAsync(s => s.Key == "default_language");
            if (langSetting != null) language = langSetting.Value;
        }
        catch { }

        var title = Path.GetFileNameWithoutExtension(sourcePath);
        var meeting = new Meeting
        {
            Title = title,
            Platform = "Imported",
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            AudioPath = destPath,
            DurationMs = durationMs,
            Language = language,
        };

        await using var dbSave = new AppDbContext();
        dbSave.Meetings.Add(meeting);
        await dbSave.SaveChangesAsync();

        await LoadMeetingsAsync();

        // Select the newly created meeting
        SelectedMeeting = Meetings.FirstOrDefault(m => m.Id == meeting.Id);
    }

    [RelayCommand]
    private async Task DeleteMeetingAsync(MeetingItemViewModel? meeting)
    {
        if (meeting == null) return;

        if (!await ConfirmDialogAsync(
            "Delete Meeting",
            $"Are you sure you want to delete \"{meeting.Title}\"?",
            "This action cannot be undone. The recording file will remain on disk.",
            "Delete", "#f38ba8"))
            return;

        // Cancel and remove any jobs for this meeting
        MeetingItemViewModel.RaiseMeetingDeleted(meeting.Id);

        await using var db = new AppDbContext();
        var entity = await db.Meetings.FindAsync(meeting.Id);
        if (entity != null)
        {
            db.Meetings.Remove(entity);
            await db.SaveChangesAsync();
        }

        Meetings.Remove(meeting);
        if (SelectedMeeting == meeting)
            SelectedMeeting = null;
    }
}

public partial class MeetingItemViewModel : ViewModelBase
{
    public string Id { get; }

    [ObservableProperty]
    private string _title;

    public string Platform { get; }
    public DateTime StartedAt { get; }
    public string Duration { get; }
    public string AudioPath { get; }

    [ObservableProperty]
    private string _language;

    public bool HasTranscript { get; }
    public bool HasSummary { get; }

    /// <summary>Available languages for the dropdown, loaded from settings.</summary>
    public ObservableCollection<string> AvailableLanguages { get; } = new(LoadLanguageCodes());

    private static List<string> LoadLanguageCodes()
    {
        try
        {
            using var db = new AppDbContext();
            var setting = db.AppSettings.FirstOrDefault(s => s.Key == "enabled_languages");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                return setting.Value.Split(',').ToList();
        }
        catch { }
        return ["en", "vi"];
    }

    [ObservableProperty]
    private string _transcriptText = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private bool _isSummarizing;

    [ObservableProperty]
    private bool _isBusy; // set by auto-process pipeline

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _selectedTabIndex;

    // Audio playback
    private IAudioPlaybackService? _player;
    private CancellationTokenSource? _playbackTimerCts;
    private bool _isSeeking;

    [ObservableProperty]
    private bool _isPlaybackActive; // true when audio is loaded (playing or paused)

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPlaybackPaused;

    [ObservableProperty]
    private string _playbackPosition = "00:00 / 00:00";

    [ObservableProperty]
    private double _playbackCurrentSeconds;

    [ObservableProperty]
    private double _playbackTotalSeconds;

    private bool _langInitDone;

    public MeetingItemViewModel(Meeting meeting)
    {
        Id = meeting.Id;
        _title = string.IsNullOrEmpty(meeting.Title) ? $"Meeting {meeting.StartedAt:g}" : meeting.Title;
        Platform = meeting.Platform ?? "Other";
        StartedAt = meeting.StartedAt;
        AudioPath = meeting.AudioPath ?? string.Empty;
        _language = meeting.Language;

        if (meeting.DurationMs.HasValue)
        {
            var ts = TimeSpan.FromMilliseconds(meeting.DurationMs.Value);
            Duration = ts.ToString(@"hh\:mm\:ss");
        }
        else
        {
            Duration = "--:--:--";
        }

        HasTranscript = meeting.Transcripts.Count > 0;
        HasSummary = meeting.Summaries.Count > 0;

        if (HasTranscript)
            TranscriptText = meeting.Transcripts.First().FullText ?? string.Empty;
        if (HasSummary)
            SummaryText = meeting.Summaries.First().Content;

        _langInitDone = true;
    }

    partial void OnLanguageChanged(string value)
    {
        if (!_langInitDone) return;
        _ = SaveLanguageAsync(value);
    }

    private async Task SaveLanguageAsync(string language)
    {
        try
        {
            await using var db = new AppDbContext();
            var meeting = await db.Meetings.FindAsync(Id);
            if (meeting != null)
            {
                meeting.Language = language;
                await db.SaveChangesAsync();
                StatusMessage = $"Language changed to {language}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save language: {ex.Message}";
        }
    }

    private static Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;
        return null;
    }

    [RelayCommand]
    private async Task SaveTranscriptAsync()
    {
        if (string.IsNullOrEmpty(TranscriptText)) return;

        try
        {
            await using var db = new AppDbContext();
            var transcript = await db.Transcripts
                .Where(t => t.MeetingId == Id)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (transcript != null)
            {
                transcript.FullText = TranscriptText;
                await db.SaveChangesAsync();
                Log.Information("Transcript saved for meeting {Id}", Id);
                StatusMessage = "Transcript saved";
            }
            else
            {
                // No transcript record yet — create one
                db.Transcripts.Add(new Transcript
                {
                    MeetingId = Id,
                    Engine = "manual",
                    Language = Language,
                    FullText = TranscriptText,
                });
                await db.SaveChangesAsync();
                Log.Information("New transcript created for meeting {Id}", Id);
                StatusMessage = "Transcript saved";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save transcript for {Id}", Id);
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyTranscriptAsync()
    {
        if (string.IsNullOrEmpty(TranscriptText)) return;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(TranscriptText);
            StatusMessage = "Transcript copied to clipboard";
        }
    }

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        if (string.IsNullOrEmpty(SummaryText)) return;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(SummaryText);
            StatusMessage = "Summary copied to clipboard";
        }
    }

    private static async Task<string?> ShowSaveDialogAsync(string suggestedName, string extension, string filterName, string[] patterns)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var dialog = new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                DefaultExtension = extension,
                FileTypeChoices =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType(filterName) { Patterns = patterns }
                ]
            };

            var result = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(dialog);
            return result?.Path.LocalPath;
        }
        return null;
    }

    [RelayCommand]
    private async Task ExportAudioAsync()
    {
        if (string.IsNullOrEmpty(AudioPath) || !File.Exists(AudioPath)) return;

        try
        {
            var destPath = await ShowSaveDialogAsync(
                Path.GetFileName(AudioPath),
                Path.GetExtension(AudioPath).TrimStart('.'),
                "Audio files", ["*.wav", "*.mp3"]);

            if (destPath != null)
            {
                File.Copy(AudioPath, destPath, true);
                StatusMessage = $"Audio exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportTranscriptAsync()
    {
        if (string.IsNullOrEmpty(TranscriptText)) return;

        try
        {
            var safeName = Title.Length > 50 ? Title[..50] : Title;
            var destPath = await ShowSaveDialogAsync(
                $"{safeName} - Transcript.txt", "txt",
                "Text files", ["*.txt", "*.md"]);

            if (destPath != null)
            {
                await File.WriteAllTextAsync(destPath, TranscriptText);
                StatusMessage = $"Transcript exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportSummaryAsync()
    {
        if (string.IsNullOrEmpty(SummaryText)) return;

        try
        {
            var safeName = Title.Length > 50 ? Title[..50] : Title;
            var destPath = await ShowSaveDialogAsync(
                $"{safeName} - Summary.txt", "txt",
                "Text files", ["*.txt", "*.md"]);

            if (destPath != null)
            {
                await File.WriteAllTextAsync(destPath, SummaryText);
                StatusMessage = $"Summary exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!IsPlaybackPaused)
                        StopAudioInternal();
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
    private void StopAudio()
    {
        StopAudioInternal();
    }

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

    partial void OnPlaybackCurrentSecondsChanged(double value)
    {
        if (_isSeeking && _player?.IsInitialized == true)
        {
            _player.CurrentTime = TimeSpan.FromSeconds(value);
        }
    }

    public void BeginSeek() => _isSeeking = true;

    public void EndSeek()
    {
        if (_player?.IsInitialized == true)
        {
            _player.CurrentTime = TimeSpan.FromSeconds(PlaybackCurrentSeconds);
        }
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

    private async Task UpdatePlaybackPositionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _player?.IsInitialized == true)
        {
            try
            {
                var current = _player.CurrentTime;
                var total = _player.TotalTime;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!_isSeeking)
                        PlaybackCurrentSeconds = current.TotalSeconds;
                    PlaybackPosition = $"{current:mm\\:ss} / {total:mm\\:ss}";
                });
                await Task.Delay(250, ct);
            }
            catch { break; }
        }
    }

    /// <summary>Raised when user clicks Transcribe, Summarize, etc. to enqueue via MainWindowViewModel.</summary>
    public static event EventHandler<(string meetingId, string action)>? JobRequested;

    /// <summary>Raised when a meeting is deleted, so MainWindowViewModel can cancel/remove related jobs.</summary>
    public static event EventHandler<string>? MeetingDeleted;
    public static void RaiseMeetingDeleted(string meetingId) => MeetingDeleted?.Invoke(null, meetingId);

    [RelayCommand]
    private void Transcribe()
    {
        if (string.IsNullOrEmpty(AudioPath)) return;
        StatusMessage = "Queued for transcription...";
        JobRequested?.Invoke(this, (Id, "transcribe"));
    }

    [RelayCommand]
    private void Summarize()
    {
        if (string.IsNullOrEmpty(TranscriptText))
        {
            StatusMessage = "No transcript to summarize. Transcribe first.";
            return;
        }
        StatusMessage = "Queued for summarization...";
        JobRequested?.Invoke(this, (Id, "summarize"));
    }

    [RelayCommand]
    private async Task ProcessAllAsync()
    {
        if (string.IsNullOrEmpty(AudioPath)) return;

        // Check "don't ask again" setting
        bool skipDialog = false;
        string savedSteps = "tsm"; // default all
        try
        {
            await using var db = new AppDbContext();
            var skipSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
            var stepsSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
            skipDialog = skipSetting?.Value == "true";
            if (stepsSetting != null) savedSteps = stepsSetting.Value;
        }
        catch { }

        if (skipDialog)
        {
            StatusMessage = "Queued for processing...";
            JobRequested?.Invoke(this, (Id, $"pipeline:{savedSteps}"));
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        var doTranscribe = savedSteps.Contains('t');
        var doSpeakers = savedSteps.Contains('s');
        var doSummarize = savedSteps.Contains('m');
        var dontAskAgain = false;
        var confirmed = false;

        var dialog = new Avalonia.Controls.Window
        {
            Title = "Smart Process",
            Width = 420,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brush.Parse("#1e1e2e"),
        };

        var content = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(28, 24),
            Spacing = 16,
        };

        content.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "Select processing steps:",
            FontSize = 14, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
        });

        var chkTranscribe = new Avalonia.Controls.CheckBox
        {
            IsChecked = doTranscribe, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Content = new Avalonia.Controls.StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "1. Transcribe audio", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#89b4fa"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new Avalonia.Controls.TextBlock { Text = "Convert audio to text using Whisper", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#bac2de") },
                }
            }
        };

        var chkSpeakers = new Avalonia.Controls.CheckBox
        {
            IsChecked = doSpeakers, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Content = new Avalonia.Controls.StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "2. Identify speakers", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#f9e2af"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new Avalonia.Controls.TextBlock { Text = "AI reformats transcript as dialog with speaker labels", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#bac2de") },
                }
            }
        };

        var chkSummarize = new Avalonia.Controls.CheckBox
        {
            IsChecked = doSummarize, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Content = new Avalonia.Controls.StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = "3. Summarize with AI", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#a6e3a1"), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new Avalonia.Controls.TextBlock { Text = "Generate a meeting summary from the transcript", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#bac2de") },
                }
            }
        };

        content.Children.Add(chkTranscribe);
        content.Children.Add(chkSpeakers);
        content.Children.Add(chkSummarize);

        // Separator
        content.Children.Add(new Avalonia.Controls.Border
        {
            Height = 1, Background = Avalonia.Media.Brush.Parse("#313244"),
        });

        var chkDontAsk = new Avalonia.Controls.CheckBox
        {
            IsChecked = false, Foreground = Avalonia.Media.Brush.Parse("#7f849c"),
            Content = new Avalonia.Controls.TextBlock
            {
                Text = "Don't ask again (reset in Settings)",
                FontSize = 12,
            },
        };
        content.Children.Add(chkDontAsk);

        var buttons = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            Background = Avalonia.Media.Brush.Parse("#313244"),
            Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        var startBtn = new Avalonia.Controls.Button
        {
            Content = "Start Processing",
            Background = Avalonia.Media.Brush.Parse("#cba6f7"),
            Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"),
            Padding = new Avalonia.Thickness(24, 10),
            CornerRadius = new Avalonia.CornerRadius(8),
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        startBtn.Click += (_, _) =>
        {
            doTranscribe = chkTranscribe.IsChecked == true;
            doSpeakers = chkSpeakers.IsChecked == true;
            doSummarize = chkSummarize.IsChecked == true;
            dontAskAgain = chkDontAsk.IsChecked == true;
            confirmed = true;
            dialog.Close();
        };

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(startBtn);
        content.Children.Add(buttons);
        dialog.Content = content;

        await dialog.ShowDialog(desktop.MainWindow);
        if (!confirmed) return;

        // Save "don't ask again" preference
        var steps = $"{(doTranscribe ? "t" : "")}{(doSpeakers ? "s" : "")}{(doSummarize ? "m" : "")}";
        if (dontAskAgain)
        {
            try
            {
                await using var db = new AppDbContext();
                var skipSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
                if (skipSetting != null) skipSetting.Value = "true";
                else db.AppSettings.Add(new Models.AppSettings { Key = "smart_process_skip_dialog", Value = "true" });

                var stepsSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
                if (stepsSetting != null) stepsSetting.Value = steps;
                else db.AppSettings.Add(new Models.AppSettings { Key = "smart_process_steps", Value = steps });

                await db.SaveChangesAsync();
            }
            catch { }
        }

        StatusMessage = "Queued for processing...";
        JobRequested?.Invoke(this, (Id, $"pipeline:{steps}"));
    }

    [RelayCommand]
    private void IdentifySpeakers()
    {
        if (string.IsNullOrEmpty(TranscriptText))
        {
            StatusMessage = "No transcript. Transcribe first.";
            return;
        }
        StatusMessage = "Queued for speaker identification...";
        JobRequested?.Invoke(this, (Id, "identify_speakers"));
    }

}
