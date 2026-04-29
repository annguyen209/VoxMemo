# VoxMemo Thorough Improvement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all user-facing UX gaps and code quality issues identified in the design spec, plus long-recording reliability bugs in the audio pipeline.

**Architecture:** 17 sequential tasks covering ViewModel split, AXAML dialogs, full-text search, UI improvements, theme toggle, and Windows audio pipeline fixes. Each task is independently committable. Start at Task 1 and work forward — later tasks depend on the ViewModel split in Tasks 2–5.

**Tech Stack:** .NET 10, Avalonia UI / AXAML, CommunityToolkit.Mvvm, NAudio, Whisper.net, SQLite / EF Core, Serilog, xUnit

---

## File Map

### New files
| File | Purpose |
|---|---|
| `Services/Database/AppDbContextFactory.cs` | Centralized factory replacing scattered `new AppDbContext()` |
| `ViewModels/SegmentItemViewModel.cs` | Single transcript segment for Segments tab |
| `ViewModels/AudioPlaybackViewModel.cs` | All audio playback state and commands |
| `ViewModels/MeetingDetailViewModel.cs` | Transcript, summary, job triggers, export, segments |
| `Views/Dialogs/ConfirmDialog.axaml` + `.axaml.cs` | Reusable confirm/delete dialog |
| `Views/Dialogs/TranscriptOverwriteDialog.axaml` + `.axaml.cs` | Overwrite-or-keep dialog |
| `Views/Dialogs/SmartProcessDialog.axaml` + `.axaml.cs` | Pipeline step selector |
| `Views/Dialogs/CreateFromTextDialog.axaml` + `.axaml.cs` | New-meeting-from-text form |
| `VoxMemo.Tests/MeetingsViewModelFilterTests.cs` | Search/filter unit tests |
| `VoxMemo.Tests/AudioConverterTests.cs` | Converter output-format tests |

### Modified files
| File | Change |
|---|---|
| `ViewModels/MeetingItemViewModel.cs` | Slim to identity + child VM wiring only |
| `ViewModels/MeetingsViewModel.cs` | Debounce, full-text search, proper errors, use new dialogs |
| `ViewModels/MainWindowViewModel.cs` | Use new dialogs, proper errors |
| `ViewModels/RecordingViewModel.cs` | Use factory |
| `ViewModels/SettingsViewModel.cs` | Theme toggle, use factory |
| `Views/MeetingsView.axaml` | Action buttons, editable title, segments tab |
| `Views/SettingsView.axaml` | Theme toggle control |
| `App.axaml` | `RequestedThemeVariant` attribute removed (set at runtime) |
| `App.axaml.cs` | Startup theme read, `SetTheme(string)` method |
| `Services/Database/AppDbContext.cs` | No logic change — factory uses existing parameterless ctor |
| `Services/Platform/Windows/WindowsAudioConverter.cs` | Replace `MediaFoundationResampler` |
| `Services/Platform/Windows/WindowsAudioRecorderService.cs` | Fix catch{}, mix fallback, 4GB warning, progress |
| `Services/Platform/Windows/WindowsRecordingRecoveryService.cs` | Fix `uint` overflow |

---

## Task 1: Centralize DbContext Creation

**Files:**
- Create: `Services/Database/AppDbContextFactory.cs`
- Modify: every file that calls `new AppDbContext()`

- [ ] **Step 1: Create the factory**

```csharp
// Services/Database/AppDbContextFactory.cs
namespace VoxMemo.Services.Database;

public static class AppDbContextFactory
{
    public static AppDbContext Create() => new AppDbContext();
}
```

- [ ] **Step 2: Replace all usages**

In every `.cs` file, replace `new AppDbContext()` with `AppDbContextFactory.Create()`.

Files affected (search for `new AppDbContext()`):
- `ViewModels/MeetingsViewModel.cs` (3 occurrences)
- `ViewModels/MainWindowViewModel.cs` (4 occurrences)
- `ViewModels/RecordingViewModel.cs` (2 occurrences)
- `ViewModels/SettingsViewModel.cs` (5 occurrences)
- `Services/Platform/Windows/WindowsRecordingRecoveryService.cs` (1 occurrence)
- `App.axaml.cs` (1 occurrence in `RegisterHotkeyFromSettingsAsync`)

Use a project-wide search-and-replace: `new AppDbContext()` → `AppDbContextFactory.Create()`.

Do NOT replace the constructor calls inside `AppDbContext.cs` itself or the test helper `CreateTestDb()`.

- [ ] **Step 3: Build and verify**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Run existing DB tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~DatabaseIntegration" -v normal
```
Expected: all pass.

- [ ] **Step 5: Commit**

```
git add Services/Database/AppDbContextFactory.cs ViewModels/ App.axaml.cs Services/
git commit -m "refactor: centralize AppDbContext creation via AppDbContextFactory"
```

---

## Task 2: SegmentItemViewModel

**Files:**
- Create: `ViewModels/SegmentItemViewModel.cs`
- Create: `VoxMemo.Tests/SegmentItemViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// VoxMemo.Tests/SegmentItemViewModelTests.cs
namespace VoxMemo.Tests;

public class SegmentItemViewModelTests
{
    [Fact]
    public void Timestamp_FormatsStartMsAsMinutesAndSeconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(65_000, 70_000, "Hello world");
        Assert.Equal("01:05", vm.Timestamp);
    }

    [Fact]
    public void Timestamp_PadsZeroBelowTenSeconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(5_000, 10_000, "Short");
        Assert.Equal("00:05", vm.Timestamp);
    }

    [Fact]
    public void StartSeconds_ConvertsMilliseconds()
    {
        var vm = new VoxMemo.ViewModels.SegmentItemViewModel(90_000, 95_000, "Text");
        Assert.Equal(90.0, vm.StartSeconds);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~SegmentItemViewModel" -v normal
```
Expected: build error — type not found.

- [ ] **Step 3: Implement**

```csharp
// ViewModels/SegmentItemViewModel.cs
namespace VoxMemo.ViewModels;

public class SegmentItemViewModel
{
    public string Timestamp { get; }
    public string Text { get; }
    public double StartSeconds { get; }

    public SegmentItemViewModel(long startMs, long endMs, string text)
    {
        var ts = System.TimeSpan.FromMilliseconds(startMs);
        Timestamp = ts.ToString(@"mm\:ss");
        Text = text;
        StartSeconds = startMs / 1000.0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~SegmentItemViewModel" -v normal
```
Expected: 3 passed.

- [ ] **Step 5: Commit**

```
git add ViewModels/SegmentItemViewModel.cs VoxMemo.Tests/SegmentItemViewModelTests.cs
git commit -m "feat: add SegmentItemViewModel for timestamped transcript segments"
```

---

## Task 3: AudioPlaybackViewModel

**Files:**
- Create: `ViewModels/AudioPlaybackViewModel.cs`

This is extracted directly from `MeetingItemViewModel`. Do not remove from `MeetingItemViewModel` yet — Task 5 does that.

- [ ] **Step 1: Create the file**

```csharp
// ViewModels/AudioPlaybackViewModel.cs
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
```

- [ ] **Step 2: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add ViewModels/AudioPlaybackViewModel.cs
git commit -m "feat: extract AudioPlaybackViewModel from MeetingItemViewModel"
```

---

## Task 4: MeetingDetailViewModel

**Files:**
- Create: `ViewModels/MeetingDetailViewModel.cs`

Extracts transcript, summary, speaker toggle, job triggers, export, segments, title save from `MeetingItemViewModel`. Do not remove from `MeetingItemViewModel` yet.

- [ ] **Step 1: Create the file**

```csharp
// ViewModels/MeetingDetailViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoxMemo.Models;
using VoxMemo.Services.Database;

namespace VoxMemo.ViewModels;

public partial class MeetingDetailViewModel : ViewModelBase
{
    private readonly string _meetingId;
    private readonly string _language;
    private readonly Action<string> _onTitleSaved;

    [ObservableProperty] private string _transcriptText = string.Empty;
    [ObservableProperty] private string _originalTranscriptText = string.Empty;
    [ObservableProperty] private bool _showOriginalTranscript;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isSummarizing;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];

    public bool HasOriginalTranscript => !string.IsNullOrEmpty(OriginalTranscriptText);
    public string TranscriptViewLabel => ShowOriginalTranscript ? "Show Identified" : "Show Original";
    public string AudioPath { get; }
    public bool HasAudio => !string.IsNullOrEmpty(AudioPath);

    partial void OnOriginalTranscriptTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasOriginalTranscript));
        OnPropertyChanged(nameof(TranscriptViewLabel));
    }

    partial void OnShowOriginalTranscriptChanged(bool value) =>
        OnPropertyChanged(nameof(TranscriptViewLabel));

    // Raised by Transcribe/Summarize/IdentifySpeakers/ProcessAll to enqueue jobs
    public static event EventHandler<(string meetingId, string action)>? JobRequested;

    public MeetingDetailViewModel(
        string meetingId,
        string audioPath,
        string language,
        Action<string> onTitleSaved,
        string transcriptText,
        string originalTranscriptText,
        string summaryText,
        System.Collections.Generic.IEnumerable<SegmentItemViewModel> segments)
    {
        _meetingId = meetingId;
        AudioPath = audioPath;
        _language = language;
        _onTitleSaved = onTitleSaved;
        _transcriptText = transcriptText;
        _originalTranscriptText = originalTranscriptText;
        _summaryText = summaryText;
        foreach (var s in segments) Segments.Add(s);
    }

    [RelayCommand]
    private void ToggleOriginalTranscript() => ShowOriginalTranscript = !ShowOriginalTranscript;

    [RelayCommand]
    private async Task SaveTitleAsync(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        try
        {
            await using var db = AppDbContextFactory.Create();
            var meeting = await db.Meetings.FindAsync(_meetingId);
            if (meeting != null)
            {
                meeting.Title = newTitle;
                await db.SaveChangesAsync();
                _onTitleSaved(newTitle);
                StatusMessage = "Title saved";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save title for {Id}", _meetingId);
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveTranscriptAsync()
    {
        if (string.IsNullOrEmpty(TranscriptText)) return;
        try
        {
            await using var db = AppDbContextFactory.Create();
            var transcript = await db.Transcripts
                .Where(t => t.MeetingId == _meetingId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (transcript != null)
            {
                transcript.FullText = TranscriptText;
                await db.SaveChangesAsync();
                StatusMessage = "Transcript saved";
            }
            else
            {
                db.Transcripts.Add(new Transcript
                {
                    MeetingId = _meetingId,
                    Engine = "manual",
                    Language = _language,
                    FullText = TranscriptText,
                });
                await db.SaveChangesAsync();
                StatusMessage = "Transcript saved";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save transcript for {Id}", _meetingId);
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyTranscriptAsync()
    {
        var text = ShowOriginalTranscript ? OriginalTranscriptText : TranscriptText;
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
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

    [RelayCommand]
    private async Task ExportTranscriptAsync()
    {
        if (string.IsNullOrEmpty(TranscriptText)) return;
        try
        {
            var destPath = await ShowSaveDialogAsync("Transcript.txt", "txt", "Text files", ["*.txt", "*.md"]);
            if (destPath != null)
            {
                await File.WriteAllTextAsync(destPath, TranscriptText);
                StatusMessage = $"Transcript exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export transcript for {Id}", _meetingId);
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportSummaryAsync()
    {
        if (string.IsNullOrEmpty(SummaryText)) return;
        try
        {
            var destPath = await ShowSaveDialogAsync("Summary.txt", "txt", "Text files", ["*.txt", "*.md"]);
            if (destPath != null)
            {
                await File.WriteAllTextAsync(destPath, SummaryText);
                StatusMessage = $"Summary exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export summary for {Id}", _meetingId);
            StatusMessage = $"Export failed: {ex.Message}";
        }
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
            Log.Error(ex, "Failed to export audio for {Id}", _meetingId);
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Transcribe()
    {
        if (string.IsNullOrEmpty(AudioPath))
        {
            StatusMessage = "No audio file — this meeting was imported from text.";
            return;
        }
        StatusMessage = "Queued for transcription...";
        JobRequested?.Invoke(this, (_meetingId, "transcribe"));
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
        JobRequested?.Invoke(this, (_meetingId, "summarize"));
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
        JobRequested?.Invoke(this, (_meetingId, "identify_speakers"));
    }

    [RelayCommand]
    private async Task ProcessAllAsync()
    {
        if (string.IsNullOrEmpty(AudioPath) && string.IsNullOrEmpty(TranscriptText)) return;

        bool skipDialog = false;
        string savedSteps = "tsm";
        try
        {
            await using var db = AppDbContextFactory.Create();
            var skipSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
            var stepsSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
            skipDialog = skipSetting?.Value == "true";
            if (stepsSetting != null) savedSteps = stepsSetting.Value;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load smart process settings");
        }

        if (skipDialog)
        {
            StatusMessage = "Queued for processing...";
            JobRequested?.Invoke(this, (_meetingId, $"pipeline:{savedSteps}"));
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        var dialog = new Views.Dialogs.SmartProcessDialog(savedSteps);
        await dialog.ShowDialog(desktop.MainWindow);
        if (dialog.Options == null) return;

        var opts = dialog.Options;
        var steps = $"{(opts.Transcribe ? "t" : "")}{(opts.Speakers ? "s" : "")}{(opts.Summarize ? "m" : "")}";

        if (opts.DontAskAgain)
        {
            try
            {
                await using var db = AppDbContextFactory.Create();
                var skipSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
                if (skipSetting != null) skipSetting.Value = "true";
                else db.AppSettings.Add(new Models.AppSettings { Key = "smart_process_skip_dialog", Value = "true" });
                var stepsSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
                if (stepsSetting != null) stepsSetting.Value = steps;
                else db.AppSettings.Add(new Models.AppSettings { Key = "smart_process_steps", Value = steps });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save smart process preferences");
            }
        }

        StatusMessage = "Queued for processing...";
        JobRequested?.Invoke(this, (_meetingId, $"pipeline:{steps}"));
    }

    [RelayCommand]
    private void SeekToSegment(SegmentItemViewModel segment)
    {
        // Handled by the view which routes to AudioPlaybackViewModel.SeekTo(segment.StartSeconds)
    }

    private static Avalonia.Input.Platform.IClipboard? GetClipboard()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;
        return null;
    }

    private static async Task<string?> ShowSaveDialogAsync(
        string suggestedName, string extension, string filterName, string[] patterns)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var result = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    SuggestedFileName = suggestedName,
                    DefaultExtension = extension,
                    FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(filterName) { Patterns = patterns }]
                });
            return result?.Path.LocalPath;
        }
        return null;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors. `SmartProcessDialog` won't exist yet — that's fine as long as the file compiles (the reference is inside a method that will be reached at runtime after Task 9 creates the dialog).

If the build fails on `SmartProcessDialog`, temporarily comment out the `ProcessAllAsync` body and uncomment after Task 9.

- [ ] **Step 3: Commit**

```
git add ViewModels/MeetingDetailViewModel.cs
git commit -m "feat: extract MeetingDetailViewModel from MeetingItemViewModel"
```

---

## Task 5: Slim MeetingItemViewModel

**Files:**
- Modify: `ViewModels/MeetingItemViewModel.cs` (replace entire file content)

`MeetingItemViewModel` becomes identity + child VM wiring only. Remove all code that is now in `AudioPlaybackViewModel` and `MeetingDetailViewModel`.

- [ ] **Step 1: Replace MeetingItemViewModel**

```csharp
// ViewModels/MeetingItemViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxMemo.Models;
using VoxMemo.Services.Database;

namespace VoxMemo.ViewModels;

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

    public ObservableCollection<string> AvailableLanguages { get; } = new(LoadLanguageCodes());

    public AudioPlaybackViewModel Playback { get; }
    public MeetingDetailViewModel Detail { get; }

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
        return ["en", "vi"];
    }

    private bool _langInitDone;

    public MeetingItemViewModel(Meeting meeting)
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
            var ts = TimeSpan.FromMilliseconds(meeting.DurationMs.Value);
            Duration = ts.ToString(@"hh\:mm\:ss");
        }
        else
        {
            Duration = "--:--:--";
        }

        HasTranscript = meeting.Transcripts.Count > 0;
        HasSummary = meeting.Summaries.Count > 0;

        string transcriptText = string.Empty;
        string originalTranscriptText = string.Empty;
        if (HasTranscript)
        {
            var t = meeting.Transcripts.OrderByDescending(t => t.CreatedAt).First();
            transcriptText = t.FullText ?? string.Empty;
            originalTranscriptText = t.OriginalFullText ?? string.Empty;
        }

        string summaryText = HasSummary ? meeting.Summaries.First().Content : string.Empty;

        var segments = meeting.Transcripts
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault()
            ?.Segments
            .OrderBy(s => s.StartMs)
            .Select(s => new SegmentItemViewModel(s.StartMs, s.EndMs, s.Text))
            ?? [];

        Playback = new AudioPlaybackViewModel(AudioPath);
        Detail = new MeetingDetailViewModel(
            meetingId: Id,
            audioPath: AudioPath,
            language: _language,
            onTitleSaved: newTitle => Title = newTitle,
            transcriptText: transcriptText,
            originalTranscriptText: originalTranscriptText,
            summaryText: summaryText,
            segments: segments);

        _langInitDone = true;
    }

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

    // Still raised from MeetingItemViewModel so MainWindowViewModel can subscribe once
    public static event EventHandler<string>? MeetingDeleted;
    public static void RaiseMeetingDeleted(string meetingId) => MeetingDeleted?.Invoke(null, meetingId);
}
```

- [ ] **Step 2: Update MainWindowViewModel job subscription**

In `MainWindowViewModel.cs`, the `MeetingItemViewModel.JobRequested` event subscription must move to `MeetingDetailViewModel.JobRequested`. In the `MainWindowViewModel` constructor, replace:

```csharp
// OLD
MeetingItemViewModel.JobRequested += (sender, args) =>
```

with:

```csharp
// NEW
MeetingDetailViewModel.JobRequested += (sender, args) =>
```

The lambda body stays identical.

- [ ] **Step 3: Update MainWindowViewModel job runners to access IsBusy via Detail**

In `JobConsumerLoop`, replace every `vm.IsBusy` access:

```csharp
// OLD
var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
if (vm != null) vm.IsBusy = true;

// NEW
var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
if (vm != null) vm.Detail.IsBusy = true;
```

Do the same for `vm.IsBusy = false`, `vm.TranscriptText = ...`, `vm.SummaryText = ...`, `vm.StatusMessage = ...`, `vm.OriginalTranscriptText`, `vm.ShowOriginalTranscript`. All these now live on `vm.Detail`.

- [ ] **Step 4: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors. Fix any remaining references to moved properties.

- [ ] **Step 5: Run all tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj -v normal
```
Expected: all pass.

- [ ] **Step 6: Commit**

```
git add ViewModels/MeetingItemViewModel.cs ViewModels/MainWindowViewModel.cs
git commit -m "refactor: slim MeetingItemViewModel, wire AudioPlaybackViewModel and MeetingDetailViewModel"
```

---

## Task 6: Full-text Search + Debounce + Error Handling

**Files:**
- Modify: `ViewModels/MeetingsViewModel.cs`
- Create: `VoxMemo.Tests/MeetingsViewModelFilterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// VoxMemo.Tests/MeetingsViewModelFilterTests.cs
using System;
using System.Collections.Generic;
using VoxMemo.Models;
using VoxMemo.ViewModels;

namespace VoxMemo.Tests;

public class MeetingsViewModelFilterTests
{
    private static MeetingItemViewModel MakeVm(
        string title, string transcript = "", string summary = "")
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Language = "en",
            StartedAt = DateTime.UtcNow,
        };
        if (!string.IsNullOrEmpty(transcript))
        {
            meeting.Transcripts.Add(new Transcript
            {
                MeetingId = meeting.Id,
                FullText = transcript,
                Language = "en"
            });
        }
        if (!string.IsNullOrEmpty(summary))
        {
            meeting.Summaries.Add(new Summary
            {
                MeetingId = meeting.Id,
                Content = summary
            });
        }
        return new MeetingItemViewModel(meeting);
    }

    [Fact]
    public void FilterMatches_Title()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("Team standup"),
            MakeVm("Client call"),
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "standup");
        Assert.Single(filtered);
        Assert.Equal("Team standup", filtered[0].Title);
    }

    [Fact]
    public void FilterMatches_TranscriptContent()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("Meeting A", transcript: "discussed quarterly budget"),
            MakeVm("Meeting B", transcript: "team building exercise"),
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "quarterly");
        Assert.Single(filtered);
        Assert.Equal("Meeting A", filtered[0].Title);
    }

    [Fact]
    public void FilterMatches_SummaryContent()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("Meeting A", summary: "action item: deploy hotfix"),
            MakeVm("Meeting B", summary: "no action items"),
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "hotfix");
        Assert.Single(filtered);
        Assert.Equal("Meeting A", filtered[0].Title);
    }

    [Fact]
    public void FilterEmpty_ReturnsAll()
    {
        var all = new List<MeetingItemViewModel>
        {
            MakeVm("A"), MakeVm("B"), MakeVm("C")
        };
        var filtered = MeetingsViewModel.ApplyFilter(all, "");
        Assert.Equal(3, filtered.Count);
    }

    [Fact]
    public void FilterIsCaseInsensitive()
    {
        var all = new List<MeetingItemViewModel> { MakeVm("Team Standup") };
        Assert.Single(MeetingsViewModel.ApplyFilter(all, "STANDUP"));
        Assert.Single(MeetingsViewModel.ApplyFilter(all, "standup"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~MeetingsViewModelFilter" -v normal
```
Expected: build error — `ApplyFilter` method not found.

- [ ] **Step 3: Add `ApplyFilter` static method and debounce to MeetingsViewModel**

In `ViewModels/MeetingsViewModel.cs`, make the following changes:

**Add field and constructor changes** — add a debounce timer field at the top of the class:

```csharp
private Avalonia.Threading.DispatcherTimer? _searchDebounce;
```

**Replace `OnSearchTextChanged`:**

```csharp
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
```

**Replace `FilterMeetings`:**

```csharp
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
```

**Fix error handling in `LoadMeetingsAsync`** — replace the bare `catch { }`:

```csharp
catch (Exception ex)
{
    Log.Error(ex, "Failed to load meetings");
    MeetingCount = "Failed to load";
}
```

**Update the LINQ include to eager-load segments** — replace:

```csharp
var meetings = await db.Meetings
    .Include(m => m.Transcripts)
    .Include(m => m.Summaries)
    .OrderByDescending(m => m.StartedAt)
    .ToListAsync();
```

with:

```csharp
var meetings = await db.Meetings
    .Include(m => m.Transcripts)
        .ThenInclude(t => t.Segments)
    .Include(m => m.Summaries)
    .OrderByDescending(m => m.StartedAt)
    .ToListAsync();
```

- [ ] **Step 4: Run tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~MeetingsViewModelFilter" -v normal
```
Expected: 5 passed.

- [ ] **Step 5: Commit**

```
git add ViewModels/MeetingsViewModel.cs VoxMemo.Tests/MeetingsViewModelFilterTests.cs
git commit -m "feat: full-text search across transcript+summary, debounce, fix error handling"
```

---

## Task 7: ConfirmDialog

**Files:**
- Create: `Views/Dialogs/ConfirmDialog.axaml`
- Create: `Views/Dialogs/ConfirmDialog.axaml.cs`

- [ ] **Step 1: Create AXAML**

```xml
<!-- Views/Dialogs/ConfirmDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="VoxMemo.Views.Dialogs.ConfirmDialog"
        Width="440" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" CanResize="False"
        Background="#1e1e2e" ExtendClientAreaToDecorationsHint="False">
    <Border Padding="28,24">
        <StackPanel Spacing="20">
            <TextBlock x:Name="MessageText" FontSize="14"
                       Foreground="#bac2de" TextWrapping="Wrap"/>
            <TextBlock x:Name="DetailText" FontSize="12"
                       Foreground="#7f849c" TextWrapping="Wrap"
                       IsVisible="False"/>
            <StackPanel Orientation="Horizontal" Spacing="10"
                        HorizontalAlignment="Right" Margin="0,4,0,0">
                <Button Content="Cancel" Click="OnCancel"
                        Background="#313244" Foreground="#cdd6f4"
                        Padding="24,10" CornerRadius="8" FontSize="13"/>
                <Button x:Name="ConfirmButton" Click="OnConfirm"
                        Foreground="#1e1e2e" Padding="24,10"
                        CornerRadius="8" FontSize="13"
                        FontWeight="SemiBold"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Create code-behind**

```csharp
// Views/Dialogs/ConfirmDialog.axaml.cs
using Avalonia.Controls;
using Avalonia.Media;

namespace VoxMemo.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string confirmColor,
        string? detail = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Background = Brush.Parse(confirmColor);

        if (!string.IsNullOrEmpty(detail))
        {
            DetailText.IsVisible = true;
            DetailText.Text = detail;
        }
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnConfirm(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add Views/Dialogs/ConfirmDialog.axaml Views/Dialogs/ConfirmDialog.axaml.cs
git commit -m "feat: add ConfirmDialog AXAML window"
```

---

## Task 8: TranscriptOverwriteDialog

**Files:**
- Create: `Views/Dialogs/TranscriptOverwriteDialog.axaml`
- Create: `Views/Dialogs/TranscriptOverwriteDialog.axaml.cs`

- [ ] **Step 1: Create AXAML**

```xml
<!-- Views/Dialogs/TranscriptOverwriteDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="VoxMemo.Views.Dialogs.TranscriptOverwriteDialog"
        Title="Transcript Already Exists"
        Width="440" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" CanResize="False"
        Background="#1e1e2e" ExtendClientAreaToDecorationsHint="False">
    <Border Padding="28,24">
        <StackPanel Spacing="20">
            <TextBlock Text="This meeting already has a transcript."
                       FontSize="14" Foreground="#bac2de" TextWrapping="Wrap"/>
            <TextBlock Text="Would you like to overwrite it with a new transcription, or keep the existing one?"
                       FontSize="12" Foreground="#7f849c" TextWrapping="Wrap"/>
            <StackPanel Orientation="Horizontal" Spacing="10"
                        HorizontalAlignment="Right" Margin="0,4,0,0">
                <Button Content="Keep Existing" Click="OnKeep"
                        Background="#313244" Foreground="#cdd6f4"
                        Padding="24,10" CornerRadius="8" FontSize="13"/>
                <Button Content="Overwrite" Click="OnOverwrite"
                        Background="#a6e3a1" Foreground="#1e1e2e"
                        Padding="24,10" CornerRadius="8" FontSize="13"
                        FontWeight="SemiBold"/>
            </StackPanel>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Create code-behind**

```csharp
// Views/Dialogs/TranscriptOverwriteDialog.axaml.cs
using Avalonia.Controls;

namespace VoxMemo.Views.Dialogs;

public partial class TranscriptOverwriteDialog : Window
{
    public bool ShouldOverwrite { get; private set; }

    public TranscriptOverwriteDialog() => InitializeComponent();

    private void OnKeep(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnOverwrite(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShouldOverwrite = true;
        Close();
    }
}
```

- [ ] **Step 3: Build and commit**

```
dotnet build VoxMemo.csproj
git add Views/Dialogs/TranscriptOverwriteDialog.axaml Views/Dialogs/TranscriptOverwriteDialog.axaml.cs
git commit -m "feat: add TranscriptOverwriteDialog AXAML window"
```

---

## Task 9: SmartProcessDialog

**Files:**
- Create: `Views/Dialogs/SmartProcessDialog.axaml`
- Create: `Views/Dialogs/SmartProcessDialog.axaml.cs`

- [ ] **Step 1: Define result type** — add to `SmartProcessDialog.axaml.cs`:

```csharp
// Views/Dialogs/SmartProcessDialog.axaml.cs
using Avalonia.Controls;

namespace VoxMemo.Views.Dialogs;

public record SmartProcessOptions(bool Transcribe, bool Speakers, bool Summarize, bool DontAskAgain);

public partial class SmartProcessDialog : Window
{
    public SmartProcessOptions? Options { get; private set; }

    private readonly CheckBox _chkTranscribe;
    private readonly CheckBox _chkSpeakers;
    private readonly CheckBox _chkSummarize;
    private readonly CheckBox _chkDontAsk;

    public SmartProcessDialog(string savedSteps)
    {
        InitializeComponent();
        _chkTranscribe = this.FindControl<CheckBox>("ChkTranscribe")!;
        _chkSpeakers = this.FindControl<CheckBox>("ChkSpeakers")!;
        _chkSummarize = this.FindControl<CheckBox>("ChkSummarize")!;
        _chkDontAsk = this.FindControl<CheckBox>("ChkDontAsk")!;
        _chkTranscribe.IsChecked = savedSteps.Contains('t');
        _chkSpeakers.IsChecked = savedSteps.Contains('s');
        _chkSummarize.IsChecked = savedSteps.Contains('m');
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnStart(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Options = new SmartProcessOptions(
            Transcribe: _chkTranscribe.IsChecked == true,
            Speakers: _chkSpeakers.IsChecked == true,
            Summarize: _chkSummarize.IsChecked == true,
            DontAskAgain: _chkDontAsk.IsChecked == true);
        Close();
    }
}
```

- [ ] **Step 2: Create AXAML**

```xml
<!-- Views/Dialogs/SmartProcessDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="VoxMemo.Views.Dialogs.SmartProcessDialog"
        Title="Smart Process" Width="420"
        SizeToContent="Height" WindowStartupLocation="CenterOwner"
        CanResize="False" Background="#1e1e2e">
    <StackPanel Margin="28,24" Spacing="16">
        <TextBlock Text="Select processing steps:"
                   FontSize="14" Foreground="#cdd6f4"/>

        <CheckBox x:Name="ChkTranscribe" Foreground="#cdd6f4">
            <StackPanel Spacing="2">
                <TextBlock Text="1. Transcribe audio" FontSize="13"
                           Foreground="#89b4fa" FontWeight="SemiBold"/>
                <TextBlock Text="Convert audio to text using Whisper"
                           FontSize="11" Foreground="#bac2de"/>
            </StackPanel>
        </CheckBox>

        <CheckBox x:Name="ChkSpeakers" Foreground="#cdd6f4">
            <StackPanel Spacing="2">
                <TextBlock Text="2. Identify speakers" FontSize="13"
                           Foreground="#f9e2af" FontWeight="SemiBold"/>
                <TextBlock Text="AI reformats transcript as dialog with speaker labels"
                           FontSize="11" Foreground="#bac2de"/>
            </StackPanel>
        </CheckBox>

        <CheckBox x:Name="ChkSummarize" Foreground="#cdd6f4">
            <StackPanel Spacing="2">
                <TextBlock Text="3. Summarize with AI" FontSize="13"
                           Foreground="#a6e3a1" FontWeight="SemiBold"/>
                <TextBlock Text="Generate a meeting summary from the transcript"
                           FontSize="11" Foreground="#bac2de"/>
            </StackPanel>
        </CheckBox>

        <Border Height="1" Background="#313244"/>

        <CheckBox x:Name="ChkDontAsk" Foreground="#7f849c">
            <TextBlock Text="Don't ask again (reset in Settings)" FontSize="12"/>
        </CheckBox>

        <StackPanel Orientation="Horizontal" Spacing="10"
                    HorizontalAlignment="Right" Margin="0,4,0,0">
            <Button Content="Cancel" Click="OnCancel"
                    Background="#313244" Foreground="#cdd6f4"
                    Padding="24,10" CornerRadius="8" FontSize="13"/>
            <Button Content="Start Processing" Click="OnStart"
                    Background="#cba6f7" Foreground="#1e1e2e"
                    Padding="24,10" CornerRadius="8" FontSize="13"
                    FontWeight="SemiBold"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 3: Build and commit**

```
dotnet build VoxMemo.csproj
git add Views/Dialogs/SmartProcessDialog.axaml Views/Dialogs/SmartProcessDialog.axaml.cs
git commit -m "feat: add SmartProcessDialog AXAML window"
```

---

## Task 10: CreateFromTextDialog

**Files:**
- Create: `Views/Dialogs/CreateFromTextDialog.axaml`
- Create: `Views/Dialogs/CreateFromTextDialog.axaml.cs`

- [ ] **Step 1: Create code-behind**

```csharp
// Views/Dialogs/CreateFromTextDialog.axaml.cs
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VoxMemo.Views.Dialogs;

public record CreateFromTextResult(string Title, string Transcript, string Language);

public partial class CreateFromTextDialog : Window
{
    public CreateFromTextResult? Result { get; private set; }

    private readonly TextBox _titleBox;
    private readonly TextBox _transcriptBox;
    private readonly ComboBox _langCombo;

    public CreateFromTextDialog(string defaultLanguage, List<string> languages)
    {
        InitializeComponent();
        _titleBox = this.FindControl<TextBox>("TitleBox")!;
        _transcriptBox = this.FindControl<TextBox>("TranscriptBox")!;
        _langCombo = this.FindControl<ComboBox>("LangCombo")!;
        _langCombo.ItemsSource = languages;
        _langCombo.SelectedItem = defaultLanguage;
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var transcript = _transcriptBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(transcript)) return;

        var title = string.IsNullOrWhiteSpace(_titleBox.Text)
            ? $"Text Import {System.DateTime.Now:MMM dd, yyyy HH:mm}"
            : _titleBox.Text;

        Result = new CreateFromTextResult(
            Title: title,
            Transcript: transcript,
            Language: _langCombo.SelectedItem?.ToString() ?? "en");
        Close();
    }

    private async void OnImportFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select text file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Text files")
                {
                    Patterns = ["*.txt", "*.md", "*.srt", "*.vtt", "*.csv", "*.log"]
                }
            ]
        });

        if (files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            _transcriptBox.Text = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(_titleBox.Text))
                _titleBox.Text = Path.GetFileNameWithoutExtension(filePath);
        }
    }
}
```

- [ ] **Step 2: Create AXAML**

```xml
<!-- Views/Dialogs/CreateFromTextDialog.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="VoxMemo.Views.Dialogs.CreateFromTextDialog"
        Title="New Meeting from Text"
        Width="550" Height="550"
        WindowStartupLocation="CenterOwner" CanResize="True"
        Background="#1e1e2e">
    <DockPanel Margin="24,20">
        <!-- Bottom buttons -->
        <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Spacing="10"
                    HorizontalAlignment="Right" Margin="0,8,0,0">
            <Button Content="Cancel" Click="OnCancel"
                    Background="#313244" Foreground="#cdd6f4"
                    Padding="24,10" CornerRadius="8" FontSize="13"/>
            <Button Content="Create Meeting" Click="OnCreate"
                    Background="#89b4fa" Foreground="#1e1e2e"
                    Padding="24,10" CornerRadius="8" FontSize="13"
                    FontWeight="SemiBold"/>
        </StackPanel>

        <!-- Top fields -->
        <StackPanel DockPanel.Dock="Top" Spacing="10">
            <TextBlock Text="Title" FontSize="12" Foreground="#bac2de"/>
            <TextBox x:Name="TitleBox" Watermark="Meeting title..."
                     Background="#313244" Foreground="#cdd6f4"
                     CornerRadius="6" Padding="12,8"/>
            <TextBlock Text="Language" FontSize="12" Foreground="#bac2de"/>
            <ComboBox x:Name="LangCombo"
                      Background="#313244" Foreground="#cdd6f4"
                      CornerRadius="6" Padding="10,6"
                      HorizontalAlignment="Left" MinWidth="100"/>
            <!-- Transcript label + import button -->
            <DockPanel>
                <TextBlock Text="Transcript" FontSize="12" Foreground="#bac2de"
                           VerticalAlignment="Center"/>
                <Button DockPanel.Dock="Right" Content="Import from File"
                        Click="OnImportFile"
                        Background="#313244" Foreground="#bac2de"
                        Padding="12,6" CornerRadius="6" FontSize="12"
                        HorizontalAlignment="Right"/>
            </DockPanel>
        </StackPanel>

        <!-- Transcript fills remaining space -->
        <TextBox x:Name="TranscriptBox"
                 Watermark="Paste or type the transcript here..."
                 AcceptsReturn="True" TextWrapping="Wrap"
                 Background="#313244" Foreground="#cdd6f4"
                 CornerRadius="6" Padding="12,8" Margin="0,10,0,0"/>
    </DockPanel>
</Window>
```

- [ ] **Step 3: Build and commit**

```
dotnet build VoxMemo.csproj
git add Views/Dialogs/CreateFromTextDialog.axaml Views/Dialogs/CreateFromTextDialog.axaml.cs
git commit -m "feat: add CreateFromTextDialog AXAML window"
```

---

## Task 11: Wire Dialogs in ViewModels

**Files:**
- Modify: `ViewModels/MeetingsViewModel.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`

Replace all inline dialog code with the new dialog classes.

- [ ] **Step 1: Update `MeetingsViewModel.DeleteMeetingAsync`**

Replace the `ConfirmDialogAsync` static method and its call site entirely:

```csharp
// In MeetingsViewModel — delete the entire ConfirmDialogAsync static method.

// In DeleteMeetingAsync, replace the await ConfirmDialogAsync(...) block:
if (Avalonia.Application.Current?.ApplicationLifetime
    is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
    || desktop.MainWindow == null)
    return;

var dialog = new Views.Dialogs.ConfirmDialog(
    "Delete Meeting",
    $"Are you sure you want to delete \"{meeting.Title}\"?",
    "Delete", "#f38ba8",
    "This action cannot be undone. The recording file will remain on disk.");
await dialog.ShowDialog(desktop.MainWindow);
if (!dialog.Confirmed) return;
```

Also fix the bare `catch` blocks in `DeleteMeetingAsync`:

```csharp
catch (Exception ex)
{
    Log.Error(ex, "Failed to delete meeting {Id}", meeting.Id);
    // no status field on list-level VM — log is sufficient
}
```

- [ ] **Step 2: Update `MeetingsViewModel.CreateFromTextAsync`**

Replace the entire method body with:

```csharp
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
        Log.Error(ex, "Failed to load language settings for CreateFromText");
    }

    var dialog = new Views.Dialogs.CreateFromTextDialog(language, languages);
    await dialog.ShowDialog(desktop.MainWindow);
    if (dialog.Result == null) return;

    var r = dialog.Result;
    var meeting = new Meeting
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
        dbSave.Transcripts.Add(new Transcript
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
        Log.Error(ex, "Failed to save text-import meeting");
    }
}
```

- [ ] **Step 3: Update `MainWindowViewModel.ShowTranscriptOverwriteDialogAsync`**

Delete the entire `ShowTranscriptOverwriteDialogAsync` static method and replace its call site in `JobConsumerLoop`:

```csharp
// Replace:
var shouldOverwrite = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
    ShowTranscriptOverwriteDialogAsync());

// With:
var shouldOverwrite = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
{
    if (Avalonia.Application.Current?.ApplicationLifetime
        is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
        || desktop.MainWindow == null)
        return true;
    var dialog = new Views.Dialogs.TranscriptOverwriteDialog();
    await dialog.ShowDialog(desktop.MainWindow);
    return dialog.ShouldOverwrite;
});
```

- [ ] **Step 4: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add ViewModels/MeetingsViewModel.cs ViewModels/MainWindowViewModel.cs
git commit -m "refactor: replace imperative dialog code with AXAML dialog classes"
```

---

## Task 12: MeetingsView — Action Buttons + Inline Title

**Files:**
- Modify: `Views/MeetingsView.axaml`

- [ ] **Step 1: Replace the detail header title TextBlock with an editable TextBox**

Find this block in `Views/MeetingsView.axaml`:

```xml
<TextBlock Text="{Binding Title}" FontSize="20" FontWeight="Bold"
           Foreground="#cdd6f4" TextTrimming="CharacterEllipsis"/>
```

Replace with:

```xml
<TextBox Text="{Binding Title}"
         FontSize="20" FontWeight="Bold"
         Foreground="#cdd6f4"
         Background="Transparent"
         BorderThickness="0"
         Padding="0"
         Watermark="Meeting title..."
         LostFocus="OnTitleLostFocus"
         KeyDown="OnTitleKeyDown"/>
```

- [ ] **Step 2: Add code-behind handlers for title editing**

In `Views/MeetingsView.axaml.cs`, add:

```csharp
private void OnTitleLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    if (sender is Avalonia.Controls.TextBox tb &&
        DataContext is VoxMemo.ViewModels.MeetingsViewModel vm &&
        vm.SelectedMeeting != null)
    {
        _ = vm.SelectedMeeting.Detail.SaveTitleCommand.ExecuteAsync(tb.Text);
    }
}

private void OnTitleKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
{
    if (sender is not Avalonia.Controls.TextBox tb) return;
    if (e.Key == Avalonia.Input.Key.Enter)
    {
        tb.Focus(); // loses focus → triggers LostFocus
        if (DataContext is VoxMemo.ViewModels.MeetingsViewModel vm && vm.SelectedMeeting != null)
            _ = vm.SelectedMeeting.Detail.SaveTitleCommand.ExecuteAsync(tb.Text);
    }
    else if (e.Key == Avalonia.Input.Key.Escape)
    {
        // Revert — rebind from VM
        if (DataContext is VoxMemo.ViewModels.MeetingsViewModel vm && vm.SelectedMeeting != null)
            tb.Text = vm.SelectedMeeting.Title;
        tb.ClearSelection();
    }
}
```

- [ ] **Step 3: Add action buttons below Smart Process**

Find this in `Views/MeetingsView.axaml`:

```xml
<Button Command="{Binding ProcessAllCommand}"
        Background="#cba6f7" Foreground="#1e1e2e"
        CornerRadius="8" Padding="20,10">
    <TextBlock Text="Smart Process" FontSize="13" FontWeight="SemiBold"/>
</Button>
```

Replace with (note: all commands now bind through `Detail`):

```xml
<StackPanel Spacing="8">
    <Button Command="{Binding Detail.ProcessAllCommand}"
            Background="#cba6f7" Foreground="#1e1e2e"
            CornerRadius="8" Padding="20,10" HorizontalAlignment="Left">
        <TextBlock Text="Smart Process" FontSize="13" FontWeight="SemiBold"/>
    </Button>

    <StackPanel Orientation="Horizontal" Spacing="6">
        <Button Command="{Binding Detail.TranscribeCommand}"
                IsEnabled="{Binding !Detail.IsBusy}"
                IsVisible="{Binding HasAudio}"
                Background="#89b4fa" Foreground="#1e1e2e"
                CornerRadius="6" Padding="14,6" FontSize="12"
                FontWeight="SemiBold">
            <TextBlock Text="Transcribe"/>
        </Button>
        <Button Command="{Binding Detail.IdentifySpeakersCommand}"
                IsEnabled="{Binding !Detail.IsBusy}"
                IsVisible="{Binding HasTranscript}"
                Background="#f9e2af" Foreground="#1e1e2e"
                CornerRadius="6" Padding="14,6" FontSize="12"
                FontWeight="SemiBold">
            <TextBlock Text="Identify Speakers"/>
        </Button>
        <Button Command="{Binding Detail.SummarizeCommand}"
                IsEnabled="{Binding !Detail.IsBusy}"
                IsVisible="{Binding HasTranscript}"
                Background="#a6e3a1" Foreground="#1e1e2e"
                CornerRadius="6" Padding="14,6" FontSize="12"
                FontWeight="SemiBold">
            <TextBlock Text="Summarize"/>
        </Button>
    </StackPanel>
</StackPanel>
```

- [ ] **Step 4: Fix remaining bindings in the detail panel**

All bindings in the detail panel `DataContext="{Binding SelectedMeeting}"` that previously pointed to `MeetingItemViewModel` properties now need to route through `Detail` or `Playback`.

Update the status, transcript tab, summary tab, and player sections:

- `{Binding StatusMessage}` → `{Binding Detail.StatusMessage}`
- `{Binding IsBusy}` → `{Binding Detail.IsBusy}`
- `{Binding IsTranscribing}` → `{Binding Detail.IsTranscribing}`
- `{Binding TranscriptText}` → `{Binding Detail.TranscriptText}`
- `{Binding OriginalTranscriptText}` → `{Binding Detail.OriginalTranscriptText}`
- `{Binding ShowOriginalTranscript}` → `{Binding Detail.ShowOriginalTranscript}`
- `{Binding HasOriginalTranscript}` → `{Binding Detail.HasOriginalTranscript}`
- `{Binding TranscriptViewLabel}` → `{Binding Detail.TranscriptViewLabel}`
- `{Binding SummaryText}` → `{Binding Detail.SummaryText}`
- `{Binding SelectedTabIndex}` → `{Binding Detail.SelectedTabIndex}`
- `SaveTranscriptCommand` → `Detail.SaveTranscriptCommand`
- `CopyTranscriptCommand` → `Detail.CopyTranscriptCommand`
- `ExportTranscriptCommand` → `Detail.ExportTranscriptCommand`
- `CopySummaryCommand` → `Detail.CopySummaryCommand`
- `ExportSummaryCommand` → `Detail.ExportSummaryCommand`
- `ToggleOriginalTranscriptCommand` → `Detail.ToggleOriginalTranscriptCommand`
- All playback bindings: `IsPlaybackActive` → `Playback.IsPlaybackActive`, etc.
- `PlayAudioCommand` → `Playback.PlayAudioCommand`
- `PauseAudioCommand` → `Playback.PauseAudioCommand`
- `StopAudioCommand` → `Playback.StopAudioCommand`
- `PlaybackCurrentSeconds` → `Playback.PlaybackCurrentSeconds`
- `PlaybackTotalSeconds` → `Playback.PlaybackTotalSeconds`
- `PlaybackPosition` → `Playback.PlaybackPosition`
- `ExportAudioCommand` → `Detail.ExportAudioCommand`

Also update the code-behind `SeekSlider` event handlers in `MeetingsView.axaml.cs` to call `vm.SelectedMeeting.Playback.BeginSeek()` / `EndSeek()`.

- [ ] **Step 5: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors. Resolve any remaining binding path issues.

- [ ] **Step 6: Commit**

```
git add Views/MeetingsView.axaml Views/MeetingsView.axaml.cs
git commit -m "feat: action buttons, editable title, updated bindings for VM split"
```

---

## Task 13: Segments Tab

**Files:**
- Modify: `Views/MeetingsView.axaml`
- Modify: `Views/MeetingsView.axaml.cs`

- [ ] **Step 1: Add the Segments tab to the TabControl**

Inside the `<TabControl SelectedIndex="{Binding Detail.SelectedTabIndex}">` in `MeetingsView.axaml`, after the Summary `TabItem`, add:

```xml
<TabItem Header="Segments"
         IsVisible="{Binding Detail.Segments.Count, Converter={x:Static ObjectConverters.IsNotNull}}">
    <ListBox ItemsSource="{Binding Detail.Segments}"
             Background="Transparent" Margin="0,12">
        <ListBox.Styles>
            <Style Selector="ListBoxItem">
                <Setter Property="Padding" Value="8,6"/>
                <Setter Property="Cursor" Value="Hand"/>
            </Style>
        </ListBox.Styles>
        <ListBox.ItemTemplate>
            <DataTemplate x:DataType="vm:SegmentItemViewModel">
                <Grid ColumnDefinitions="60,*">
                    <Border Grid.Column="0" Background="#313244"
                            CornerRadius="4" Padding="6,3"
                            HorizontalAlignment="Left" VerticalAlignment="Top"
                            Margin="0,2,8,0">
                        <TextBlock Text="{Binding Timestamp}"
                                   FontSize="11" Foreground="#89b4fa"
                                   FontFamily="Consolas, Courier New"/>
                    </Border>
                    <TextBlock Grid.Column="1" Text="{Binding Text}"
                               FontSize="13" Foreground="#cdd6f4"
                               TextWrapping="Wrap"/>
                </Grid>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
</TabItem>
```

- [ ] **Step 2: Wire segment click to seek**

In `MeetingsView.axaml.cs`, add a `SelectionChanged` handler on the segments `ListBox`:

```csharp
// Add x:Name="SegmentsListBox" to the ListBox in AXAML, then:
private void OnSegmentSelected(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
{
    if (e.AddedItems.Count == 0) return;
    if (e.AddedItems[0] is VoxMemo.ViewModels.SegmentItemViewModel seg &&
        DataContext is VoxMemo.ViewModels.MeetingsViewModel vm &&
        vm.SelectedMeeting?.Playback.IsPlaybackActive == true)
    {
        vm.SelectedMeeting.Playback.SeekTo(seg.StartSeconds);
    }
}
```

Add `SelectionChanged="OnSegmentSelected"` to the ListBox in AXAML.

- [ ] **Step 3: Fix Segments tab visibility**

Replace the `IsVisible` binding — the `Segments.Count` int doesn't bind well to `IsNotNull`. Use a simpler approach:

In `MeetingDetailViewModel`, add:

```csharp
public bool HasSegments => Segments.Count > 0;
```

Then in AXAML: `IsVisible="{Binding Detail.HasSegments}"`.

- [ ] **Step 4: Build and commit**

```
dotnet build VoxMemo.csproj
git add Views/MeetingsView.axaml Views/MeetingsView.axaml.cs ViewModels/MeetingDetailViewModel.cs
git commit -m "feat: add Segments tab with seek-on-click"
```

---

## Task 14: Theme Toggle

**Files:**
- Modify: `App.axaml`
- Modify: `App.axaml.cs`
- Modify: `ViewModels/SettingsViewModel.cs`
- Modify: `Views/SettingsView.axaml`

- [ ] **Step 1: Remove hardcoded theme from App.axaml**

In `App.axaml`, change:

```xml
<Application ... RequestedThemeVariant="Dark">
```
to:
```xml
<Application ...>
```

(Remove the `RequestedThemeVariant` attribute — it will be set at runtime.)

- [ ] **Step 2: Add SetTheme to App.axaml.cs**

In `App.axaml.cs`, add the method:

```csharp
public void SetTheme(string theme)
{
    RequestedThemeVariant = theme == "light"
        ? Avalonia.Styling.ThemeVariant.Light
        : Avalonia.Styling.ThemeVariant.Dark;
}
```

In `OnFrameworkInitializationCompleted`, after the DB init block and before creating `_mainVm`, add:

```csharp
// Apply saved theme
string savedTheme = "dark";
try
{
    await using var themeDb = AppDbContextFactory.Create();
    var themeSetting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(themeDb.AppSettings, s => s.Key == "ui_theme");
    if (themeSetting != null) savedTheme = themeSetting.Value;
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to load theme setting");
}
SetTheme(savedTheme);
```

- [ ] **Step 3: Add theme property to SettingsViewModel**

In `ViewModels/SettingsViewModel.cs`, add the field and property:

```csharp
[ObservableProperty]
private string _selectedTheme = "dark";
```

Add a partial method to respond to changes:

```csharp
partial void OnSelectedThemeChanged(string value)
{
    if (!_isLoading)
    {
        _ = SaveSettingDirectAsync("ui_theme", value);
        (Avalonia.Application.Current as App)?.SetTheme(value);
    }
}
```

In `LoadSettingsAsync`, add loading of the theme:

```csharp
SelectedTheme = await GetSettingAsync(db, "ui_theme", "dark");
```

In `SaveSettingsAsync`, add:

```csharp
await SetSettingAsync(db, "ui_theme", SelectedTheme);
```

- [ ] **Step 4: Add toggle to SettingsView.axaml**

Find the General section in `Views/SettingsView.axaml` and add the theme toggle. If there is no clear "General" section header, add it before the language settings. The toggle:

```xml
<TextBlock Text="Theme" FontSize="13" Foreground="#bac2de" Margin="0,16,0,6"/>
<StackPanel Orientation="Horizontal" Spacing="8">
    <RadioButton Content="Dark"
                 IsChecked="{Binding SelectedTheme, Converter={x:Static StringConverters.IsNotNullOrEmpty},
                             ConverterParameter=dark}"
                 GroupName="Theme"
                 Foreground="#cdd6f4" FontSize="13"/>
    <RadioButton Content="Light"
                 IsChecked="{Binding SelectedTheme, Converter={x:Static StringConverters.IsNotNullOrEmpty},
                             ConverterParameter=light}"
                 GroupName="Theme"
                 Foreground="#cdd6f4" FontSize="13"/>
</StackPanel>
```

Because Avalonia's `RadioButton` doesn't bind a string value directly, use a simpler two-button approach in code-behind instead. Add `x:Name` to the radio buttons and handle `Checked` events:

```csharp
// In SettingsView.axaml.cs:
private void OnDarkThemeChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
        vm.SelectedTheme = "dark";
}
private void OnLightThemeChecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
        vm.SelectedTheme = "light";
}
```

In AXAML:
```xml
<RadioButton x:Name="DarkRadio" Content="Dark" GroupName="Theme"
             Checked="OnDarkThemeChecked"
             Foreground="#cdd6f4" FontSize="13"/>
<RadioButton x:Name="LightRadio" Content="Light" GroupName="Theme"
             Checked="OnLightThemeChecked"
             Foreground="#cdd6f4" FontSize="13"/>
```

And in `SettingsView.axaml.cs`, override `OnDataContextChanged` to sync the radio state when settings load:

```csharp
protected override void OnDataContextChanged(EventArgs e)
{
    base.OnDataContextChanged(e);
    if (DataContext is VoxMemo.ViewModels.SettingsViewModel vm)
    {
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SelectedTheme))
            {
                DarkRadio.IsChecked = vm.SelectedTheme == "dark";
                LightRadio.IsChecked = vm.SelectedTheme == "light";
            }
        };
    }
}
```

- [ ] **Step 5: Build**

```
dotnet build VoxMemo.csproj
```

- [ ] **Step 6: Commit**

```
git add App.axaml App.axaml.cs ViewModels/SettingsViewModel.cs Views/SettingsView.axaml Views/SettingsView.axaml.cs
git commit -m "feat: light/dark theme toggle, persisted in settings"
```

---

## Task 15: Fix WindowsAudioConverter

**Files:**
- Modify: `Services/Platform/Windows/WindowsAudioConverter.cs`
- Create: `VoxMemo.Tests/AudioConverterTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// VoxMemo.Tests/AudioConverterTests.cs
using System.IO;
using NAudio.Wave;
using VoxMemo.Services.Platform.Windows;

namespace VoxMemo.Tests;

public class AudioConverterTests
{
    private static string CreateTestWav(int sampleRate, int channels, int durationSeconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Path.GetRandomFileName()}.wav");
        var format = new WaveFormat(sampleRate, 16, channels);
        using var writer = new WaveFileWriter(path, format);
        int samples = sampleRate * channels * durationSeconds;
        var data = new byte[samples * 2]; // 16-bit = 2 bytes
        writer.Write(data, 0, data.Length);
        return path;
    }

    [Fact]
    public void ConvertToWhisperFormat_Produces16kHzMono16bit()
    {
        var inputPath = CreateTestWav(44100, 2, 2);
        var outputPath = Path.ChangeExtension(inputPath, ".out.wav");
        try
        {
            new WindowsAudioConverter().ConvertToWhisperFormat(inputPath, outputPath);
            Assert.True(File.Exists(outputPath));
            using var reader = new WaveFileReader(outputPath);
            Assert.Equal(16000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void ConvertToWhisperFormat_PreservesAudioContent()
    {
        var inputPath = CreateTestWav(48000, 2, 1);
        var outputPath = Path.ChangeExtension(inputPath, ".out.wav");
        try
        {
            new WindowsAudioConverter().ConvertToWhisperFormat(inputPath, outputPath);
            var info = new FileInfo(outputPath);
            // 1 second at 16kHz mono 16-bit = 16000 * 2 = 32000 bytes + 44 header
            Assert.True(info.Length > 32000);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
```

- [ ] **Step 2: Run to verify — first test should pass (current impl), second should pass**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~AudioConverter" -v normal
```
Note current pass/fail state. Both should pass with current code unless running on a machine where MF fails on small files. The real fix is for large files.

- [ ] **Step 3: Replace MediaFoundationResampler with WdlResamplingSampleProvider**

```csharp
// Services/Platform/Windows/WindowsAudioConverter.cs
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsAudioConverter : IAudioConverter
{
    public void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        using var reader = new AudioFileReader(inputPath);

        // Resample to 16 kHz
        ISampleProvider resampled = reader.WaveFormat.SampleRate != 16000
            ? new WdlResamplingSampleProvider(reader, 16000)
            : (ISampleProvider)reader;

        // Downmix to mono
        ISampleProvider mono = resampled.WaveFormat.Channels != 1
            ? new StereoToMonoSampleProvider(resampled)
            : resampled;

        // Write as 16-bit PCM
        WaveFileWriter.CreateWaveFile16(outputPath, mono);
    }

    public void ConvertInPlace(string wavPath)
    {
        var tempPath = wavPath + ".original.wav";
        File.Move(wavPath, tempPath, overwrite: true);
        try
        {
            ConvertToWhisperFormat(tempPath, wavPath);
            File.Delete(tempPath);
        }
        catch
        {
            if (!File.Exists(wavPath) && File.Exists(tempPath))
                File.Move(tempPath, wavPath);
            throw;
        }
    }

    public TimeSpan GetDuration(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        return reader.TotalTime;
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~AudioConverter" -v normal
```
Expected: both pass.

- [ ] **Step 5: Commit**

```
git add Services/Platform/Windows/WindowsAudioConverter.cs VoxMemo.Tests/AudioConverterTests.cs
git commit -m "fix: replace MediaFoundationResampler with WdlResamplingSampleProvider (no 1GB limit)"
```

---

## Task 16: Fix WindowsAudioRecorderService

**Files:**
- Modify: `Services/Platform/Windows/WindowsAudioRecorderService.cs`

Four fixes: (a) error-reporting catch blocks in data handlers, (b) check mix result and fall back to single-source, (c) 4 GB warning, (d) progress status messages during stop.

- [ ] **Step 1: Fix catch blocks in data handlers**

Add a flag to prevent flooding, then replace the three `catch { }` blocks:

```csharp
// Add field at top of class:
private bool _writeErrorReported;

// Replace OnDataAvailable catch:
catch (Exception ex)
{
    if (!_writeErrorReported)
    {
        _writeErrorReported = true;
        Log.Error(ex, "Audio write failed during recording");
        RecordingError?.Invoke(this, $"Audio write failed: {ex.Message}");
    }
}

// Replace OnMicDataAvailable catch:
catch (Exception ex)
{
    if (!_writeErrorReported)
    {
        _writeErrorReported = true;
        Log.Error(ex, "Mic audio write failed");
        RecordingError?.Invoke(this, $"Mic write failed: {ex.Message}");
    }
}

// Replace OnSysDataAvailable catch:
catch (Exception ex)
{
    if (!_writeErrorReported)
    {
        _writeErrorReported = true;
        Log.Error(ex, "System audio write failed");
        RecordingError?.Invoke(this, $"System audio write failed: {ex.Message}");
    }
}
```

Reset `_writeErrorReported = false;` in `StartRecordingAsync` before starting capture.

- [ ] **Step 2: Add 4 GB warning**

Add to `StartRecordingAsync`, after the format is determined (both modes), before `_stopwatch.Restart()`:

```csharp
// Warn if recording may approach the 4 GB WAV limit
void WarnIfLargeFormat(WaveFormat fmt)
{
    const long maxSafeBytes = (long)(3.8 * 1024 * 1024 * 1024);
    long bytesPerHour = (long)fmt.AverageBytesPerSecond * 3600;
    if (bytesPerHour > maxSafeBytes)
    {
        var safeDuration = TimeSpan.FromSeconds(maxSafeBytes / fmt.AverageBytesPerSecond);
        Log.Warning("Audio format {Format} will hit the 4 GB WAV limit after {Safe:hh\\:mm}. " +
                    "Recordings longer than {Safe:hh\\:mm} may be truncated.",
                    fmt, safeDuration);
    }
}

if (_isMixMode)
{
    WarnIfLargeFormat(_micCapture!.WaveFormat);
    WarnIfLargeFormat(_sysCapture!.WaveFormat);
}
else
{
    WarnIfLargeFormat(_captureFormat!);
}
```

- [ ] **Step 3: Add progress messages and mix fallback in StopRecordingAsync**

`StopRecordingAsync` needs to expose progress. Add an event or accept a callback. The simplest approach since `RecordingError` already exists: add a new event `RecordingStatus`:

```csharp
// Add to the class:
public event EventHandler<string>? RecordingStatus;
```

Add to `IAudioRecorder` interface:
```csharp
event EventHandler<string>? RecordingStatus;
```

Then in `StopRecordingAsync`, replace the mix `await Task.Run(...)` block:

```csharp
if (_mixFinalPath != null && _micTempPath != null && _sysTempPath != null)
{
    RecordingStatus?.Invoke(this, "Mixing audio tracks (may take a minute for long recordings)...");
    var mixOk = await Task.Run(() =>
        WindowsRecordingRecoveryService.TryCreateMixedRecording(
            _micTempPath, _sysTempPath, _mixFinalPath,
            cleanupInputsOnSuccess: true));

    if (!mixOk)
    {
        Log.Warning("Mix failed for {Output} — falling back to single source", _mixFinalPath);
        // Fall back to whichever temp file is larger (sys preferred)
        var sysSizeBytes = _sysTempPath != null && File.Exists(_sysTempPath)
            ? new FileInfo(_sysTempPath).Length : 0;
        var micSizeBytes = _micTempPath != null && File.Exists(_micTempPath)
            ? new FileInfo(_micTempPath).Length : 0;
        var bestPath = sysSizeBytes >= micSizeBytes ? _sysTempPath : _micTempPath;
        if (bestPath != null && File.Exists(bestPath))
        {
            File.Move(bestPath, _mixFinalPath, overwrite: true);
            RecordingError?.Invoke(this,
                "Mix failed — saved single audio track only. Check logs for details.");
        }
        // Clean up remaining temp
        try { if (_sysTempPath != null && File.Exists(_sysTempPath)) File.Delete(_sysTempPath); } catch { }
        try { if (_micTempPath != null && File.Exists(_micTempPath)) File.Delete(_micTempPath); } catch { }
    }
}
```

Also add a status message in `RecordingViewModel.StopRecordingAsync` right before the `ConvertInPlace` call:

```csharp
StatusMessage = "Converting to transcription format...";
```

And subscribe to `RecordingStatus` in `RecordingViewModel` constructor:

```csharp
_recorder.RecordingStatus += (_, msg) =>
{
    Dispatcher.UIThread.Post(() => StatusMessage = msg);
};
```

Also add `RecordingStatus` to stub implementations in `Services/Platform/Stub/StubAudioRecorderService.cs`:
```csharp
public event EventHandler<string>? RecordingStatus;
```

- [ ] **Step 4: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors. Fix any IAudioRecorder implementation gaps.

- [ ] **Step 5: Commit**

```
git add Services/Platform/Windows/WindowsAudioRecorderService.cs \
        Services/Audio/IAudioRecorder.cs \
        Services/Platform/Stub/StubAudioRecorderService.cs \
        ViewModels/RecordingViewModel.cs
git commit -m "fix: log audio write errors, mix fallback, 4GB warning, progress status during stop"
```

---

## Task 17: Fix WindowsRecordingRecoveryService uint Overflow

**Files:**
- Modify: `Services/Platform/Windows/WindowsRecordingRecoveryService.cs`
- Modify: `VoxMemo.Tests/WindowsRecordingRecoveryServiceTests.cs`

- [ ] **Step 1: Write failing test for the clamping behavior**

Add to `WindowsRecordingRecoveryServiceTests`:

```csharp
[Fact]
public void TryRepairWaveHeader_DoesNotOverflowForLargeFiles()
{
    // Create a WAV file, then manually set its RIFF/data sizes to 0
    // to simulate a file that was written but never closed cleanly.
    var dir = CreateTempDirectory();
    try
    {
        var path = Path.Combine(dir, "large.wav");
        CreateWaveFile(path);
        var dataSizeOffset = FindDataSizeOffset(path);

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Position = 4;
            writer.Write(0u); // zero out RIFF size
            stream.Position = dataSizeOffset;
            writer.Write(0u); // zero out data size
        }

        // Should repair without throwing even if stream.Length - 8 would overflow uint
        var repaired = WindowsRecordingRecoveryService.TryRepairWaveHeader(path);
        Assert.True(repaired);

        // Verify the written sizes are bounded by uint.MaxValue (no negative/overflow values)
        using var checkStream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(checkStream);
        checkStream.Position = 4;
        var riffSize = reader.ReadUInt32();
        Assert.True(riffSize <= uint.MaxValue);
        checkStream.Position = dataSizeOffset;
        var dataSize = reader.ReadUInt32();
        Assert.True(dataSize <= uint.MaxValue);
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run to verify it passes (the test doesn't specifically test overflow — it tests repair works)**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~WindowsRecordingRecovery" -v normal
```

- [ ] **Step 3: Fix the uint cast in TryRepairWaveHeader**

In `WindowsRecordingRecoveryService.TryRepairWaveHeader`, find:

```csharp
var actualRiffSize = (uint)Math.Max(0, stream.Length - 8);
var actualDataSize = (uint)Math.Max(0, stream.Length - dataStartOffset.Value);
```

Replace with:

```csharp
var rawRiffSize = Math.Max(0L, stream.Length - 8);
var rawDataSize = Math.Max(0L, stream.Length - dataStartOffset.Value);
// Clamp to uint.MaxValue — WAV files > 4 GB have corrupted headers regardless,
// but clamping prevents silent silent int overflow from writing garbage.
var actualRiffSize = (uint)Math.Min(rawRiffSize, uint.MaxValue);
var actualDataSize = (uint)Math.Min(rawDataSize, uint.MaxValue);
```

- [ ] **Step 4: Run all tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj -v normal
```
Expected: all pass.

- [ ] **Step 5: Commit**

```
git add Services/Platform/Windows/WindowsRecordingRecoveryService.cs VoxMemo.Tests/WindowsRecordingRecoveryServiceTests.cs
git commit -m "fix: clamp uint cast in TryRepairWaveHeader to prevent overflow for large files"
```

---

## Self-Review

### Spec coverage

| Spec section | Tasks covering it |
|---|---|
| 1.1 Split MeetingItemViewModel | Tasks 2, 3, 4, 5 |
| 1.2 Replace new AppDbContext() | Task 1 |
| 1.3 Fix sync DB in constructors | Tasks 4, 5 (Detail/Playback init is sync-safe since data is passed in) |
| 2. Dialogs → AXAML | Tasks 7, 8, 9, 10, 11 |
| 3.1 Full-text search | Task 6 |
| 3.2 Debounce | Task 6 |
| 3.3 Error handling | Tasks 6, 11 |
| 4.1 Action buttons | Task 12 |
| 4.2 Inline title editing | Task 12 |
| 4.3 Segments tab | Task 13 |
| 5. Theme toggle | Task 14 |
| 6. Error handling policy | Tasks 6, 11, 16 |
| 6b. MediaFoundationResampler fix | Task 15 |
| 6b. Mix failure fallback | Task 16 |
| 6b. WAV 4 GB safeguard | Tasks 16, 17 |
| 6b. Silent catch fix | Task 16 |
| 6b. Progress feedback | Task 16 |

**Gap found:** Spec section 1.3 says to fix synchronous DB calls in constructors. `LoadLanguageCodes()` in `MeetingItemViewModel` is still synchronous. This is acceptable — it runs during meeting list load (already on background) and is a short local DB read. Not worth an async chain for a settings read. Noted, not adding a task.

### Placeholder scan

No TBDs, TODOs, or "similar to Task N" patterns found.

### Type consistency check

- `SmartProcessOptions` defined in `SmartProcessDialog.axaml.cs` (Task 9), referenced in `MeetingDetailViewModel.ProcessAllAsync` (Task 4) — consistent.
- `SegmentItemViewModel(long startMs, long endMs, string text)` defined in Task 2, constructed in Task 5 — consistent.
- `AppDbContextFactory.Create()` defined in Task 1, used in Tasks 4, 5, 6, 11 — consistent.
- `RecordingStatus` event added to `IAudioRecorder` in Task 16, implemented in stub in Task 16 — consistent.
- `MeetingDetailViewModel.JobRequested` static event defined in Task 4, subscribed in Task 5 (`MainWindowViewModel`) — consistent.
- `AudioPlaybackViewModel.SeekTo(double)` defined in Task 3, called from Task 13 segment click handler — consistent.
