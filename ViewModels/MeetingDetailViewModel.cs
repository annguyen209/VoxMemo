using System;
using System.Collections.Generic;
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
    [ObservableProperty] private string _speakersText = string.Empty;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isSummarizing;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public bool HasSegments => Segments.Count > 0;
    public bool HasSpeakers => !string.IsNullOrEmpty(SpeakersText);
    public string AudioPath { get; }
    public bool HasAudio => !string.IsNullOrEmpty(AudioPath);

    partial void OnSpeakersTextChanged(string value) =>
        OnPropertyChanged(nameof(HasSpeakers));

    public static event EventHandler<(string meetingId, string action)>? JobRequested;

    public MeetingDetailViewModel(
        string meetingId,
        string audioPath,
        string language,
        Action<string> onTitleSaved,
        string transcriptText,
        string speakersText,
        string summaryText,
        IEnumerable<SegmentItemViewModel> segments)
    {
        _meetingId = meetingId;
        AudioPath = audioPath;
        _language = language;
        _onTitleSaved = onTitleSaved;
        _transcriptText = transcriptText;
        _speakersText = speakersText;
        _summaryText = summaryText;
        foreach (var s in segments) Segments.Add(s);
    }

    [RelayCommand]
    private async Task SaveTitleAsync(string? newTitle)
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
                // If speaker ID has been run, original is in OriginalFullText; save there.
                // Otherwise save to FullText directly.
                if (!string.IsNullOrEmpty(transcript.OriginalFullText))
                    transcript.OriginalFullText = TranscriptText;
                else
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
        if (string.IsNullOrEmpty(TranscriptText)) return;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(TranscriptText);
            StatusMessage = "Transcript copied to clipboard";
        }
    }

    [RelayCommand]
    private async Task CopySpeakersAsync()
    {
        if (string.IsNullOrEmpty(SpeakersText)) return;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(SpeakersText);
            StatusMessage = "Speaker transcript copied to clipboard";
        }
    }

    [RelayCommand]
    private async Task ExportSpeakersAsync()
    {
        if (string.IsNullOrEmpty(SpeakersText)) return;
        try
        {
            var destPath = await ShowSaveDialogAsync("Speakers.txt", "txt", "Text files", ["*.txt", "*.md"]);
            if (destPath != null)
            {
                await File.WriteAllTextAsync(destPath, SpeakersText);
                StatusMessage = $"Speaker transcript exported to {Path.GetFileName(destPath)}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export speaker transcript for {Id}", _meetingId);
            StatusMessage = $"Export failed: {ex.Message}";
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

        var processDialog = new VoxMemo.Views.Dialogs.SmartProcessDialog(savedSteps);
        await processDialog.ShowDialog(desktop.MainWindow);
        if (processDialog.Options == null) return;

        var opts = processDialog.Options;
        var steps = $"{(opts.Transcribe ? "t" : "")}{(opts.Speakers ? "s" : "")}{(opts.Summarize ? "m" : "")}";

        if (opts.DontAskAgain)
        {
            try
            {
                await using var db2 = AppDbContextFactory.Create();
                var s1 = await db2.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
                if (s1 != null) s1.Value = "true";
                else db2.AppSettings.Add(new VoxMemo.Models.AppSettings { Key = "smart_process_skip_dialog", Value = "true" });
                var s2 = await db2.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
                if (s2 != null) s2.Value = steps;
                else db2.AppSettings.Add(new VoxMemo.Models.AppSettings { Key = "smart_process_steps", Value = steps });
                await db2.SaveChangesAsync();
            }
            catch (Exception ex) { Log.Error(ex, "Failed to save smart process preferences"); }
        }

        StatusMessage = "Queued for processing...";
        JobRequested?.Invoke(this, (_meetingId, $"pipeline:{steps}"));
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
