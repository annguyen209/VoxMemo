# VoxMemo — Thorough Improvement Pass Design

**Date:** 2026-04-29  
**Scope:** Big-bang improvement pass covering all user-facing UX gaps and code quality issues  
**Approach:** Everything in one branch, all areas touched once

---

## 1. ViewModel Architecture

### 1.1 Split `MeetingItemViewModel`

Current state: `MeetingItemViewModel` is ~1,200 lines handling identity, audio playback, transcript editing, export, job triggering, and speaker toggling simultaneously.

Split into three focused classes:

**`MeetingItemViewModel`** — identity and list metadata only  
Properties: `Id`, `Title`, `Platform`, `StartedAt`, `Duration`, `Language`, `HasAudio`, `HasTranscript`, `HasSummary`  
Used by the meeting list `ListBox` item template. Exposes `Detail` and `Playback` as child VMs.

**`AudioPlaybackViewModel`** — all playback state and commands  
Properties: `IsPlaybackActive`, `IsPlaying`, `IsPlaybackPaused`, `PlaybackCurrentSeconds`, `PlaybackTotalSeconds`, `PlaybackPosition`  
Commands: `PlayAudio`, `PauseAudio`, `StopAudio`  
Methods: `BeginSeek()`, `EndSeek()`, `SeekTo(double)`  
Owned by `MeetingItemViewModel`, exposed as `MeetingItemViewModel.Playback`.

**`MeetingDetailViewModel`** — transcript, summary, speaker toggle, job triggers, export  
Properties: `TranscriptText`, `OriginalTranscriptText`, `ShowOriginalTranscript`, `SummaryText`, `Segments` (for timestamped segments tab), `IsTranscribing`, `IsSummarizing`, `IsBusy`, `StatusMessage`, `SelectedTabIndex`  
Commands: `Transcribe`, `Summarize`, `IdentifySpeakers`, `ProcessAll`, `SaveTranscript`, `CopyTranscript`, `CopySummary`, `ExportAudio`, `ExportTranscript`, `ExportSummary`, `ToggleOriginalTranscript`, `SaveTitle`  
Owned by `MeetingItemViewModel`, exposed as `MeetingItemViewModel.Detail`.

### 1.2 Replace `new AppDbContext()` with `IDbContextFactory<AppDbContext>`

Register `services.AddDbContextFactory<AppDbContext>()` in `Program.cs`. Inject `IDbContextFactory<AppDbContext>` into `MainWindowViewModel`, `MeetingsViewModel`, `RecordingViewModel`, `SettingsViewModel`, and all dialog ViewModels. Replace every `await using var db = new AppDbContext()` call with `await using var db = _factory.CreateDbContext()`.

This is the correct EF Core pattern for desktop apps with concurrent access (the job queue already hits the DB from multiple tasks).

### 1.3 Fix Synchronous DB Calls in Constructors

`LoadLanguageCodes()` in `MeetingItemViewModel` and the language default load in `RecordingViewModel` constructor currently run synchronous DB queries. Move them to an `async Task InitAsync()` method called via `_ = InitAsync()` at the end of the constructor. This matches the existing pattern used by `LoadSettingsAsync` and `LoadMeetingsAsync` but makes it consistent everywhere.

---

## 2. Dialogs → AXAML

All four imperatively-built dialogs (~500 lines of layout code inside ViewModels) are extracted to proper `Window` subclasses with AXAML markup.

### Files to create

| Dialog | AXAML file | Code-behind |
|---|---|---|
| Generic confirm/delete | `Views/Dialogs/ConfirmDialog.axaml` | `ConfirmDialog.axaml.cs` |
| Transcript overwrite | `Views/Dialogs/TranscriptOverwriteDialog.axaml` | `TranscriptOverwriteDialog.axaml.cs` |
| Create meeting from text | `Views/Dialogs/CreateFromTextDialog.axaml` | `CreateFromTextDialog.axaml.cs` |
| Smart Process options | `Views/Dialogs/SmartProcessDialog.axaml` | `SmartProcessDialog.axaml.cs` |

### Pattern

Each dialog exposes a typed result via a public property set before `Close()` is called:

```csharp
// ConfirmDialog.axaml.cs
public bool Confirmed { get; private set; }

// SmartProcessDialog.axaml.cs
public SmartProcessOptions? Options { get; private set; }

// CreateFromTextDialog.axaml.cs
public CreateFromTextResult? Result { get; private set; }
```

Callers reduce from ~80 lines to 3–5 lines:

```csharp
var dialog = new ConfirmDialog("Delete Meeting", $"Delete \"{meeting.Title}\"?", "Delete", "#f38ba8");
await dialog.ShowDialog(mainWindow);
if (!dialog.Confirmed) return;
```

### Shared dialog styling

All dialogs share the same Catppuccin Mocha background (`#1e1e2e`), button styles, and corner radius. Extract shared button and dialog styles into `App.axaml` resources so they aren't duplicated across four files.

---

## 3. Meetings List: Full-text Search + Debounce

### 3.1 Full-text Search

`FilterMeetings` currently matches only `Title`, `Platform`, and date string. Extend the predicate to also match `Detail.TranscriptText` and `Detail.SummaryText`. Both strings are already in memory on `MeetingItemViewModel` (loaded at construction), so no extra DB query is needed.

Updated predicate:
```csharp
m.Title.Contains(query, OrdinalIgnoreCase) ||
m.Platform.Contains(query, OrdinalIgnoreCase) ||
m.StartedAt.ToString("MMM dd, yyyy").Contains(query, OrdinalIgnoreCase) ||
m.Detail.TranscriptText.Contains(query, OrdinalIgnoreCase) ||
m.Detail.SummaryText.Contains(query, OrdinalIgnoreCase)
```

### 3.2 Debounce

Wrap `OnSearchTextChanged` → `FilterMeetings` in a 200ms debounce. Implementation: a `DispatcherTimer` with `Interval = 200ms` that is restarted on each keystroke and calls `FilterMeetings` on `Tick`. The timer is stopped and restarted (not just restarted) to avoid accumulation.

### 3.3 Error Handling

Replace all `catch { }` with `catch (Exception ex) { Log.Error(ex, ...); StatusMessage = "..."; }`. Affected locations:
- `LoadMeetingsAsync` in `MeetingsViewModel`
- `CreateFromTextAsync` save path
- `CreateMeetingAsync` audio copy/convert path
- `DeleteMeetingAsync`
- Settings load/save in `SettingsViewModel`
- All job runners in `MainWindowViewModel`

---

## 4. Meeting Detail UX

### 4.1 Individual Action Buttons

Add `Transcribe`, `Summarize`, and `Identify Speakers` buttons to `MeetingsView.axaml` in the detail pane, below the "Smart Process" button. Arrange as a horizontal button group:

```
[Smart Process]  [Transcribe]  [Identify Speakers]  [Summarize]
```

Visibility rules:
- `Transcribe` — visible only when `HasAudio` is true
- `Summarize` — visible only when `HasTranscript` is true  
- `Identify Speakers` — visible only when `HasTranscript` is true
- All three are disabled when `IsBusy` is true

The commands (`TranscribeCommand`, `SummarizeCommand`, `IdentifySpeakersCommand`) already exist on the VM — this is purely a view change.

### 4.2 Inline Title Editing

Replace the read-only `TextBlock` in the meeting detail header with an editable `TextBox`:
- Styled to look like a heading when not focused (no border, transparent background, same font weight/size)
- On focus: shows a subtle border so the user knows it's editable
- On `LostFocus` or `Enter` key: calls `SaveTitleCommand` which writes the new title to DB via `UPDATE meetings SET title = ? WHERE id = ?`
- On `Escape`: reverts to the original title without saving

New command on `MeetingDetailViewModel`:
```csharp
[RelayCommand]
private async Task SaveTitleAsync(string newTitle)
```

`MeetingDetailViewModel` holds an `Action<string> _onTitleSaved` callback injected at construction time by `MeetingItemViewModel`. After writing to DB, `SaveTitleAsync` calls `_onTitleSaved(newTitle)` so the parent `MeetingItemViewModel.Title` (displayed in the list) updates immediately without a full list reload.

### 4.3 Timestamped Segments Tab

Add a third tab `"Segments"` to the detail `TabControl`. Visibility: only shown when `Detail.Segments.Count > 0`.

**Data:**
Add `ObservableCollection<SegmentItemViewModel> Segments` to `MeetingDetailViewModel`. `SegmentItemViewModel` has:
- `string Timestamp` — formatted as `mm:ss` from `StartMs`
- `string Text`
- `double StartSeconds` — for seeking

Segments are eager-loaded alongside meetings in `MeetingsViewModel.LoadMeetingsAsync` via `.ThenInclude`. They are passed into `MeetingDetailViewModel` at construction time — no separate lazy fetch needed.

**View:**
A `ListBox` with a two-column item template: timestamp badge on the left (monospace, Catppuccin blue), text on the right. Clicking a row calls `SeekToSegmentCommand(segment)` which calls `Playback.SeekTo(segment.StartSeconds)` — only active when `Playback.IsPlaybackActive`.

**DB query** (updated in `MeetingsViewModel.LoadMeetingsAsync`):
```csharp
.Include(m => m.Transcripts)
    .ThenInclude(t => t.Segments)
```

---

## 5. Light/Dark Theme Toggle

### 5.1 Mechanism

Avalonia `FluentTheme` supports `ThemeVariant.Light` and `ThemeVariant.Dark` via `Application.RequestedThemeVariant`. Setting this property at runtime instantly re-renders all Fluent-styled controls.

The existing Catppuccin Mocha styles (applied via `StyleInclude` in `App.axaml`) govern the app's own colors and are not affected by the Fluent theme variant. The toggle will flip structural chrome (window chrome, scrollbars, native control styling) between light and dark while the Catppuccin palette remains in effect for all app-specific elements.

### 5.2 Setting

Add key `"ui_theme"` to `app_settings` with values `"dark"` (default) or `"light"`.

On startup in `App.axaml.cs`, read the setting and call:
```csharp
RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
```

### 5.3 Settings UI

In `SettingsView.axaml`, add a toggle in the General section:

```
Theme:  [Dark ●]  [Light ○]
```

Implemented as two radio-style buttons (or a `ToggleSwitch`). Bound to `SelectedTheme` on `SettingsViewModel`. `OnSelectedThemeChanged` saves the setting and calls:
```csharp
(Application.Current as App)?.SetTheme(value);
```

`App.SetTheme(string)` updates `RequestedThemeVariant` — no restart required.

---

## 6. Error Handling Policy

Replace all silent `catch { }` blocks with this consistent pattern:

```csharp
catch (Exception ex)
{
    Log.Error(ex, "Context description for {Id}", relevantId);
    // Surface to user where there is a visible status field:
    StatusMessage = "Short human-readable description of what failed";
}
```

Exceptions that are truly safe to swallow (e.g., `File.Delete` on a temp file in a finally block) are documented with a comment: `// intentional: cleanup failure is non-fatal`.

---

## 6b. Long Recording Reliability

### Root causes

| # | Issue | Symptom |
|---|---|---|
| 1 | `MediaFoundationResampler` (COM) fails on inputs ≥ ~1 GB | "Warning: conversion failed" after stop; recording saved but not in Whisper format; transcription fails |
| 2 | `TryCreateMixedRecording` return value discarded; on failure the output file is never created | "Recording is empty" with no explanation; two temp files orphaned on disk |
| 3 | `WaveFileWriter` stores chunk sizes as `uint` (max ~4 GB); same overflow in `TryRepairWaveHeader` | Corrupted WAV header after ~3.1 hours at 48 kHz 32-bit float stereo |
| 4 | `catch { }` in all three data handlers swallows disk-full and I/O errors silently | Audio data dropped mid-recording with no user notification |
| 5 | No progress feedback during mix + convert; for 1-hour recordings this takes 1–3 minutes | App appears frozen after Stop |

### Fixes

**Fix 1 — Replace `MediaFoundationResampler` with `WdlResamplingSampleProvider`**  
`WindowsAudioConverter.ConvertToWhisperFormat` replaces the MF COM pipeline with a pure managed resample + convert chain:
```csharp
var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
var mono = resampler.ToMono();
WaveFileWriter.CreateWaveFile16(outputPath, mono);
```
No file-size limit, no COM dependency, same output quality.

**Fix 2 — Handle mix failure gracefully**  
`WindowsAudioRecorderService.StopRecordingAsync` checks the `bool` result of `TryCreateMixedRecording`. On `false`, it falls back to using whichever single-source temp file is larger (sys preferred, then mic), moves it to the output path, and includes a message in `RecordingError`: "Mix failed — saved [mic/sys] audio only."

**Fix 3 — WAV 4 GB safeguard**  
Before recording starts, estimate max temp file size:
```
bytesPerSecond = format.AverageBytesPerSecond
```
If the estimated size at 4 hours would exceed 3.8 GB, log a warning. At the 3.8 GB mark during recording, fire `RecordingError` with "Recording approaching file size limit — stop soon or audio may be truncated." (Automatic rotation is out of scope for now; the warning covers the practical case.)

Also fix the `uint` cast in `TryRepairWaveHeader`:
```csharp
// Before (overflows for files > 4 GB):
var actualRiffSize = (uint)Math.Max(0, stream.Length - 8);
// After (clamp to uint.MaxValue — file is unusable past 4 GB but header won't corrupt):
var actualRiffSize = (uint)Math.Min(stream.Length - 8, uint.MaxValue);
```

**Fix 4 — Log errors in data handlers**  
Replace `catch { }` in `OnDataAvailable`, `OnMicDataAvailable`, `OnSysDataAvailable` with:
```csharp
catch (Exception ex)
{
    Log.Error(ex, "Audio write failed");
    RecordingError?.Invoke(this, $"Audio write failed: {ex.Message}");
}
```
Fire only once per session (gate with a `_writeErrorReported` flag) to avoid flooding the UI.

**Fix 5 — Progress feedback during mix and convert**  
`StopRecordingAsync` passes a status callback into the mix and convert steps:
- Before mix: `StatusMessage = "Mixing audio tracks (may take a minute for long recordings)..."`
- Before convert: `StatusMessage = "Converting to transcription format..."`
- On completion: `StatusMessage = $"Saved: {title}"`

---

## 7. Files Changed Summary

### New files
- `Views/Dialogs/ConfirmDialog.axaml` + `.axaml.cs`
- `Views/Dialogs/TranscriptOverwriteDialog.axaml` + `.axaml.cs`
- `Views/Dialogs/CreateFromTextDialog.axaml` + `.axaml.cs`
- `Views/Dialogs/SmartProcessDialog.axaml` + `.axaml.cs`
- `ViewModels/AudioPlaybackViewModel.cs`
- `ViewModels/MeetingDetailViewModel.cs`
- `ViewModels/SegmentItemViewModel.cs`

### Modified files
- `ViewModels/MeetingItemViewModel.cs` — reduced to identity + child VM wiring
- `ViewModels/MeetingsViewModel.cs` — debounce, full-text search, error handling, dialog usage
- `ViewModels/MainWindowViewModel.cs` — dialog usage, IDbContextFactory, error handling
- `ViewModels/RecordingViewModel.cs` — IDbContextFactory, async init
- `ViewModels/SettingsViewModel.cs` — IDbContextFactory, theme toggle
- `Views/MeetingsView.axaml` — action buttons, inline title TextBox, segments tab
- `Views/SettingsView.axaml` — theme toggle control
- `App.axaml` — shared dialog styles, SetTheme method
- `App.axaml.cs` — startup theme read, SetTheme implementation
- `Program.cs` — register IDbContextFactory
- `Services/Database/AppDbContext.cs` — any schema changes for segment eager loading
- `Services/Platform/Windows/WindowsAudioConverter.cs` — replace MediaFoundationResampler with WdlResamplingSampleProvider
- `Services/Platform/Windows/WindowsAudioRecorderService.cs` — mix fallback, 4 GB warning, fix data handler catch blocks, progress status messages
- `Services/Platform/Windows/WindowsRecordingRecoveryService.cs` — fix uint overflow in TryRepairWaveHeader
