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
    private Avalonia.Threading.DispatcherTimer? _searchDebounce;

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
        _searchDebounce?.Stop();
        _searchDebounce = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            FilterMeetings();
        };
        _searchDebounce.Start();
    }

    private void FilterMeetings()
    {
        var query = SearchText?.Trim() ?? "";
        var filtered = ApplyFilter(_allMeetings, query);
        Meetings.Clear();
        foreach (var m in filtered)
            Meetings.Add(m);
        MeetingCount = _allMeetings.Count == filtered.Count
            ? $"{_allMeetings.Count} meetings"
            : $"{filtered.Count} of {_allMeetings.Count} meetings";
    }

    public static List<MeetingItemViewModel> ApplyFilter(
        List<MeetingItemViewModel> source, string query)
    {
        if (string.IsNullOrEmpty(query)) return source;
        return source.Where(m =>
            m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Platform.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.StartedAt.ToString("MMM dd, yyyy").Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Detail.TranscriptText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            m.Detail.SummaryText.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    [RelayCommand]
    private async Task LoadMeetingsAsync()
    {
        IsLoading = true;
        try
        {
            await using var db = AppDbContextFactory.Create();
            await db.Database.EnsureCreatedAsync();

            var meetings = await db.Meetings
                .Include(m => m.Transcripts)
                    .ThenInclude(t => t.Segments)
                .Include(m => m.Summaries)
                .OrderByDescending(m => m.StartedAt)
                .ToListAsync();

            _allMeetings = meetings.Select(m => new MeetingItemViewModel(m)).ToList();
            FilterMeetings();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load meetings");
            MeetingCount = "Failed to load";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateFromTextAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        string language = "en";
        List<string> languages = ["en", "vi"];
        try
        {
            await using var db = AppDbContextFactory.Create();
            var langSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "default_language");
            if (langSetting != null) language = langSetting.Value;
            var langListSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "enabled_languages");
            if (langListSetting != null && !string.IsNullOrEmpty(langListSetting.Value))
                languages = langListSetting.Value.Split(',').ToList();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load language settings for CreateFromText");
        }

        var dialog = new VoxMemo.Views.Dialogs.CreateFromTextDialog(language, languages);
        await dialog.ShowDialog(desktop.MainWindow);
        if (dialog.Result == null) return;

        var r = dialog.Result;
        var meeting = new VoxMemo.Models.Meeting
        {
            Title = r.Title,
            Platform = "Text Import",
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            Language = r.Language,
        };

        try
        {
            await using var dbSave = AppDbContextFactory.Create();
            dbSave.Meetings.Add(meeting);
            dbSave.Transcripts.Add(new VoxMemo.Models.Transcript
            {
                MeetingId = meeting.Id,
                Engine = "manual",
                Language = r.Language,
                FullText = r.Transcript,
            });
            await dbSave.SaveChangesAsync();
            await LoadMeetingsAsync();
            SelectedMeeting = Meetings.FirstOrDefault(m => m.Id == meeting.Id);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save text-import meeting");
        }
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
            await using var settingsDb = AppDbContextFactory.Create();
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
            await using var db2 = AppDbContextFactory.Create();
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

        await using var dbSave = AppDbContextFactory.Create();
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

        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        var dialog = new VoxMemo.Views.Dialogs.ConfirmDialog(
            "Delete Meeting",
            $"Are you sure you want to delete \"{meeting.Title}\"?",
            "Delete", "#f38ba8",
            "This action cannot be undone. The recording file will remain on disk.");
        await dialog.ShowDialog(desktop.MainWindow);
        if (!dialog.Confirmed) return;

        // Cancel and remove any jobs for this meeting
        MeetingItemViewModel.RaiseMeetingDeleted(meeting.Id);

        try
        {
            await using var db = AppDbContextFactory.Create();
            var entity = await db.Meetings.FindAsync(meeting.Id);
            if (entity != null)
            {
                db.Meetings.Remove(entity);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete meeting {Id}", meeting.Id);
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

    public bool HasAudio => !string.IsNullOrEmpty(AudioPath);
    public bool HasTranscript { get; }
    public bool HasSummary { get; }

    public System.Collections.ObjectModel.ObservableCollection<string> AvailableLanguages { get; }
        = new(LoadLanguageCodes());

    private static System.Collections.Generic.List<string> LoadLanguageCodes()
    {
        try
        {
            using var db = AppDbContextFactory.Create();
            var setting = db.AppSettings.FirstOrDefault(s => s.Key == "enabled_languages");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                return setting.Value.Split(',').ToList();
        }
        catch { }
        return ["auto", "en", "vi"];
    }

    private bool _langInitDone;

    public MeetingItemViewModel(VoxMemo.Models.Meeting meeting)
    {
        Id = meeting.Id;
        _title = string.IsNullOrEmpty(meeting.Title)
            ? $"Meeting {meeting.StartedAt:g}"
            : meeting.Title;
        Platform = meeting.Platform ?? "Other";
        StartedAt = meeting.StartedAt;
        AudioPath = meeting.AudioPath ?? string.Empty;
        _language = meeting.Language;

        if (meeting.DurationMs.HasValue)
        {
            var ts = System.TimeSpan.FromMilliseconds(meeting.DurationMs.Value);
            Duration = ts.ToString(@"hh\:mm\:ss");
        }
        else
        {
            Duration = "--:--:--";
        }

        HasTranscript = meeting.Transcripts.Count > 0;
        HasSummary = meeting.Summaries.Count > 0;

        string transcriptText = string.Empty;
        string speakersText = string.Empty;
        if (HasTranscript)
        {
            var t = meeting.Transcripts.OrderByDescending(t => t.CreatedAt).First();
            // If speaker ID has been run: OriginalFullText = original, FullText = speaker text
            if (!string.IsNullOrEmpty(t.OriginalFullText))
            {
                transcriptText = t.OriginalFullText;
                speakersText = t.FullText ?? string.Empty;
            }
            else
            {
                transcriptText = t.FullText ?? string.Empty;
            }
        }

        string summaryText = HasSummary ? meeting.Summaries.First().Content : string.Empty;

        var segments = (meeting.Transcripts
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault()
            ?.Segments
            .OrderBy(s => s.StartMs)
            .Select(s => new SegmentItemViewModel(s.StartMs, s.EndMs, s.Text))
            ?? System.Linq.Enumerable.Empty<SegmentItemViewModel>());

        Playback = new AudioPlaybackViewModel(AudioPath);
        Detail = new MeetingDetailViewModel(
            meetingId: Id,
            audioPath: AudioPath,
            language: _language,
            onTitleSaved: newTitle => Title = newTitle,
            transcriptText: transcriptText,
            speakersText: speakersText,
            summaryText: summaryText,
            segments: segments);

        _langInitDone = true;
    }

    public AudioPlaybackViewModel Playback { get; }
    public MeetingDetailViewModel Detail { get; }

    partial void OnLanguageChanged(string value)
    {
        if (!_langInitDone) return;
        _ = SaveLanguageAsync(value);
    }

    private async System.Threading.Tasks.Task SaveLanguageAsync(string language)
    {
        try
        {
            await using var db = AppDbContextFactory.Create();
            var meeting = await db.Meetings.FindAsync(Id);
            if (meeting != null)
            {
                meeting.Language = language;
                await db.SaveChangesAsync();
                Detail.StatusMessage = $"Language changed to {language}";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save language for {Id}", Id);
            Detail.StatusMessage = $"Failed to save language: {ex.Message}";
        }
    }

    public static event EventHandler<string>? MeetingDeleted;
    public static void RaiseMeetingDeleted(string meetingId) => MeetingDeleted?.Invoke(null, meetingId);
}
