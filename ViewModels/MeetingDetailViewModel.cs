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
    [ObservableProperty] private string _originalTranscriptText = string.Empty;
    [ObservableProperty] private bool _showOriginalTranscript;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private bool _isSummarizing;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public bool HasSegments => Segments.Count > 0;
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

    public static event EventHandler<(string meetingId, string action)>? JobRequested;

    public MeetingDetailViewModel(
        string meetingId,
        string audioPath,
        string language,
        Action<string> onTitleSaved,
        string transcriptText,
        string originalTranscriptText,
        string summaryText,
        IEnumerable<SegmentItemViewModel> segments)
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

        // SmartProcessDialog will exist after Task 9. For now, inline the dialog inline temporarily
        // and leave a TODO so Task 9 can wire it up. We preserve existing behavior.
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

        var content = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(28, 24), Spacing = 16 };
        content.Children.Add(new Avalonia.Controls.TextBlock { Text = "Select processing steps:", FontSize = 14, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4") });

        var chkTranscribe = new Avalonia.Controls.CheckBox { IsChecked = doTranscribe, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"), Content = new Avalonia.Controls.TextBlock { Text = "1. Transcribe audio", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#89b4fa") } };
        var chkSpeakers = new Avalonia.Controls.CheckBox { IsChecked = doSpeakers, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"), Content = new Avalonia.Controls.TextBlock { Text = "2. Identify speakers", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#f9e2af") } };
        var chkSummarize = new Avalonia.Controls.CheckBox { IsChecked = doSummarize, Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"), Content = new Avalonia.Controls.TextBlock { Text = "3. Summarize with AI", FontSize = 13, Foreground = Avalonia.Media.Brush.Parse("#a6e3a1") } };
        var chkDontAsk = new Avalonia.Controls.CheckBox { IsChecked = false, Foreground = Avalonia.Media.Brush.Parse("#7f849c"), Content = new Avalonia.Controls.TextBlock { Text = "Don't ask again (reset in Settings)", FontSize = 12 } };

        content.Children.Add(chkTranscribe);
        content.Children.Add(chkSpeakers);
        content.Children.Add(chkSummarize);
        content.Children.Add(new Avalonia.Controls.Border { Height = 1, Background = Avalonia.Media.Brush.Parse("#313244") });
        content.Children.Add(chkDontAsk);

        var buttons = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var cancelBtn = new Avalonia.Controls.Button { Content = "Cancel", Background = Avalonia.Media.Brush.Parse("#313244"), Foreground = Avalonia.Media.Brush.Parse("#cdd6f4"), Padding = new Avalonia.Thickness(24, 10), CornerRadius = new Avalonia.CornerRadius(8), FontSize = 13 };
        cancelBtn.Click += (_, _) => dialog.Close();
        var startBtn = new Avalonia.Controls.Button { Content = "Start Processing", Background = Avalonia.Media.Brush.Parse("#cba6f7"), Foreground = Avalonia.Media.Brush.Parse("#1e1e2e"), Padding = new Avalonia.Thickness(24, 10), CornerRadius = new Avalonia.CornerRadius(8), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        startBtn.Click += (_, _) => { doTranscribe = chkTranscribe.IsChecked == true; doSpeakers = chkSpeakers.IsChecked == true; doSummarize = chkSummarize.IsChecked == true; dontAskAgain = chkDontAsk.IsChecked == true; confirmed = true; dialog.Close(); };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(startBtn);
        content.Children.Add(buttons);
        dialog.Content = content;

        await dialog.ShowDialog(desktop.MainWindow);
        if (!confirmed) return;

        var steps = $"{(doTranscribe ? "t" : "")}{(doSpeakers ? "s" : "")}{(doSummarize ? "m" : "")}";
        if (dontAskAgain)
        {
            try
            {
                await using var db = AppDbContextFactory.Create();
                var s1 = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_skip_dialog");
                if (s1 != null) s1.Value = "true"; else db.AppSettings.Add(new AppSettings { Key = "smart_process_skip_dialog", Value = "true" });
                var s2 = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
                if (s2 != null) s2.Value = steps; else db.AppSettings.Add(new AppSettings { Key = "smart_process_steps", Value = steps });
                await db.SaveChangesAsync();
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
