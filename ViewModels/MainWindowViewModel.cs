using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoxMemo.Services.AI;
using VoxMemo.Services.Database;
using VoxMemo.Models;

namespace VoxMemo.ViewModels;

public partial class ProcessingJobViewModel : ViewModelBase
{
    public string MeetingId { get; }
    public CancellationTokenSource Cts { get; } = new();
    public DateTime CreatedAt { get; } = DateTime.Now;
    private DateTime? _startedAt;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    [ObservableProperty]
    private string _meetingTitle;

    [ObservableProperty]
    private string _status = "Queued";

    [ObservableProperty]
    private string _step = string.Empty;

    [ObservableProperty]
    private string _elapsed = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _isFailed;

    private CancellationTokenSource? _timerCts;

    public ProcessingJobViewModel(string meetingId, string meetingTitle)
    {
        MeetingId = meetingId;
        _meetingTitle = meetingTitle;
    }

    public void MarkStarted()
    {
        _startedAt = DateTime.Now;
        _timerCts = new CancellationTokenSource();
        _ = RunElapsedTimerAsync(_timerCts.Token);
    }

    private async Task RunElapsedTimerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);
                if (_startedAt.HasValue)
                {
                    var dur = DateTime.Now - _startedAt.Value;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        Elapsed = dur.TotalMinutes >= 1 ? $"{dur:m\\:ss}" : $"{dur.TotalSeconds:F0}s");
                }
            }
            catch { break; }
        }
    }

    public void MarkFinished()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
        if (_startedAt.HasValue)
        {
            var dur = DateTime.Now - _startedAt.Value;
            Elapsed = dur.TotalMinutes >= 1 ? $"{dur:m\\:ss}" : $"{dur.TotalSeconds:F1}s";
        }
    }
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConcurrentQueue<(ProcessingJobViewModel job, string meetingId, string action)> _pendingJobs = new();
    private readonly SemaphoreSlim _jobSignal = new(0);
    private bool _consumerStarted;

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private string _processingStatus = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isQueueVisible;

    public ObservableCollection<ProcessingJobViewModel> JobQueue { get; } = [];

    public RecordingViewModel Recording { get; }
    public MeetingsViewModel Meetings { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel()
    {
        Recording = new RecordingViewModel();
        Meetings = new MeetingsViewModel();
        Settings = new SettingsViewModel();
        _currentView = Recording;

        Recording.RecordingSaved += (_, meetingId) =>
        {
            _ = OnRecordingSavedAsync(meetingId);
        };

        MeetingDetailViewModel.JobRequested += (sender, args) =>
        {
            var (meetingId, action) = args;
            var detailVm = sender as MeetingDetailViewModel;
            var meetingVm = Meetings.Meetings.FirstOrDefault(m => m.Detail == detailVm);
            var title = meetingVm?.Title ?? meetingId;

             if (action.StartsWith("pipeline:"))
             {
                 var steps = action["pipeline:".Length..];
                 var hasAudio = !string.IsNullOrEmpty(detailVm?.AudioPath);
                 var hasTranscript = !string.IsNullOrEmpty(detailVm?.TranscriptText);
                 var hasSpeakers = hasTranscript && HasSpeakerLabels(detailVm!.TranscriptText);

                 // Transcribe: skip only if no audio. Dialog will ask about overwrite if transcript exists
                 if (steps.Contains('t') && hasAudio)
                     EnqueueManualJob(meetingId, title, "transcribe");
                 // Skip identify speakers if already done
                 if (steps.Contains('s') && !hasSpeakers)
                     EnqueueManualJob(meetingId, title, "identify_speakers");
                 if (steps.Contains('m'))
                     EnqueueManualJob(meetingId, title, "summarize");
             }
            else
            {
                EnqueueManualJob(meetingId, title, action);
            }
        };

        MeetingItemViewModel.MeetingDeleted += (_, meetingId) =>
        {
            var jobs = JobQueue.Where(j => j.MeetingId == meetingId).ToList();
            foreach (var j in jobs)
            {
                j.Cts.Cancel();
                j.MarkFinished();
                JobQueue.Remove(j);
            }
            if (JobQueue.Count == 0) IsQueueVisible = false;
        };
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => Recording,
            1 => Meetings,
            2 => Settings,
            _ => Recording
        };

        if (value == 1 && Meetings.SelectedMeeting?.Playback.IsPlaybackActive != true)
        {
            _ = Meetings.LoadMeetingsCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void ToggleQueue()
    {
        IsQueueVisible = !IsQueueVisible;
    }

    [RelayCommand]
    private void ClearCompletedJobs()
    {
        var done = JobQueue.Where(j => j.IsComplete || j.IsFailed).ToList();
        foreach (var j in done) JobQueue.Remove(j);
        if (JobQueue.Count == 0) IsQueueVisible = false;
    }

    [RelayCommand]
    private void ClearAllJobs()
    {
        var toRemove = JobQueue.ToList();
        foreach (var j in toRemove)
        {
            j.Cts.Cancel();
            j.MarkFinished();
        }
        JobQueue.Clear();
        IsQueueVisible = false;
    }

    [RelayCommand]
    private void RemoveJob(ProcessingJobViewModel? job)
    {
        if (job == null) return;
        if (job.IsActive)
        {
            job.Cts.Cancel();
            job.Status = "Cancelling...";
            return;
        }
        job.Cts.Cancel();
        JobQueue.Remove(job);
        if (JobQueue.Count == 0) IsQueueVisible = false;
    }

    // --- Job Queue ---

    private void EnqueueManualJob(string meetingId, string title, string action)
    {
        var jobTitle = action switch
        {
            "transcribe" => $"{title} (Transcribe)",
            "summarize" => $"{title} (Summarize)",
            "identify_speakers" => $"{title} (Identify Speakers)",
            _ => title
        };

        var job = new ProcessingJobViewModel(meetingId, jobTitle);

        // Insert after active+queued, before completed/failed
        var insertIdx = 0;
        for (int i = 0; i < JobQueue.Count; i++)
        {
            if (JobQueue[i].IsActive || (!JobQueue[i].IsComplete && !JobQueue[i].IsFailed))
            { insertIdx = i + 1; continue; }
            break;
        }
        JobQueue.Insert(insertIdx, job);
        IsQueueVisible = true;

        _pendingJobs.Enqueue((job, meetingId, action));
        _jobSignal.Release();

        if (!_consumerStarted)
        {
            _consumerStarted = true;
            _ = Task.Run(JobConsumerLoop);
        }
    }

     private async Task JobConsumerLoop()
     {
         while (true)
         {
             await _jobSignal.WaitAsync();
             if (!_pendingJobs.TryDequeue(out var entry)) continue;

             var (job, meetingId, action) = entry;
             if (job.Cts.IsCancellationRequested)
             {
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                 {
                     if (!job.IsComplete && !job.IsFailed)
                     {
                         job.IsActive = false;
                         job.IsFailed = true;
                         job.Status = "Cancelled";
                     }
                 });
                 continue;
             }

             try
             {
                 await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                 {
                     job.IsActive = true;
                     job.MarkStarted();
                     var idx = JobQueue.IndexOf(job);
                     if (idx > 0) JobQueue.Move(idx, 0);
                     var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                     if (vm != null) vm.Detail.IsBusy = true;
                 });

                 SetStatus($"Processing: {job.MeetingTitle}");

                 if (action == "transcribe")
                 {
                     // Check if transcript already exists and ask user
                     var hasExisting = await HasExistingTranscriptAsync(meetingId);
                     if (hasExisting)
                     {
                         // Must show dialog on UI thread
                         var shouldOverwrite = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                         {
                             if (Avalonia.Application.Current?.ApplicationLifetime
                                 is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                                 || desktop.MainWindow == null)
                                 return true;
                             var dlg = new VoxMemo.Views.Dialogs.TranscriptOverwriteDialog();
                             await dlg.ShowDialog(desktop.MainWindow);
                             return dlg.ShouldOverwrite;
                         });
                         if (!shouldOverwrite)
                         {
                             // User chose to skip - mark as complete without processing
                             await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                             {
                                 job.IsActive = false;
                                 job.IsComplete = true;
                                 job.Status = "Skipped";
                                 job.Step = "Transcript already exists";
                                 job.MarkFinished();
                                 var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                                 if (vm != null)
                                 {
                                     vm.Detail.IsBusy = false;
                                     vm.Detail.StatusMessage = "Transcription skipped - existing transcript retained";
                                 }
                             });
                             ShowTrayNotification("VoxMemo", $"{job.MeetingTitle} - transcription skipped (existing retained)");
                             SetStatus("", false);
                             continue;
                         }
                     }
                     await RunManualTranscribeAsync(meetingId, job);
                 }
                 else if (action == "summarize")
                     await RunManualSummarizeAsync(meetingId, job);
                 else if (action == "identify_speakers")
                     await RunIdentifySpeakersAsync(meetingId, job);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    job.IsActive = false;
                    job.IsComplete = true;
                    job.Status = "Complete";
                    job.MarkFinished();
                    var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                    if (vm != null) vm.Detail.IsBusy = false;
                });

                ShowTrayNotification("VoxMemo", $"{job.MeetingTitle} completed ({job.Elapsed})");
                SetStatus("", false);
            }
            catch (OperationCanceledException)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    job.IsActive = false;
                    job.IsFailed = true;
                    job.Status = "Cancelled";
                    job.MarkFinished();
                    var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                    if (vm != null) vm.Detail.IsBusy = false;
                });
                SetStatus("", false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Job failed for {MeetingId} action={Action}", meetingId, action);
                
                // Check if we should retry
                var isRetryable = IsRetryableError(ex);
                if (isRetryable && job.RetryCount < job.MaxRetries)
                {
                    job.RetryCount++;
                    var delayMs = (int)Math.Pow(2, job.RetryCount) * 1000; // Exponential backoff: 2s, 4s, 8s
                    Log.Warning("Job {MeetingId} failed, retry {RetryNum}/{MaxRetries} after {Delay}ms", 
                        meetingId, job.RetryCount, job.MaxRetries, delayMs);
                    
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        job.Status = $"Retry {job.RetryCount}/{job.MaxRetries}...";
                        job.Step = $"Retrying in {delayMs/1000}s";
                    });
                    
                    await Task.Delay(delayMs);
                    
                    // Re-enqueue the job
                    _pendingJobs.Enqueue((job, meetingId, action));
                    _jobSignal.Release();
                    continue;
                }
                
                // Friendly error for common cases
                var errorMsg = ex.Message.Length > 150 ? ex.Message[..150] + "..." : ex.Message;
                ShowTrayNotification("VoxMemo", $"{job.MeetingTitle} failed: {errorMsg}");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    job.IsActive = false;
                    job.IsFailed = true;
                    job.Status = "Failed";
                    job.Step = ex.Message;
                    job.MarkFinished();
                    var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                    if (vm != null)
                    {
                        vm.Detail.IsBusy = false;
                        vm.Detail.StatusMessage = $"Error: {errorMsg}";
                    }
                });
                SetStatus("", false);
            }
        }
    }

    // --- Job Runners ---

     private async Task RunManualTranscribeAsync(string meetingId, ProcessingJobViewModel job)
     {
         string? whisperModel = null;
         string? audioPath = null;
         string language = "en";
         await using (var db = AppDbContextFactory.Create())
         {
             var wmSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "whisper_model");
             whisperModel = wmSetting?.Value;
             var meeting = await db.Meetings.FindAsync(meetingId);
             if (meeting == null) return;
             audioPath = meeting.AudioPath;
             language = meeting.Language;
         }

         if (string.IsNullOrEmpty(audioPath)) return;

         var model = whisperModel ?? "tiny";
         UpdateJob(job, "Transcribing...", $"Model: {model}");

         var service = new Services.Transcription.WhisperTranscriptionService();
         var result = await service.TranscribeAsync(audioPath, language, model, job.Cts.Token);

         await using var dbSave = AppDbContextFactory.Create();
         
         // Delete existing transcripts for this meeting to replace with new one
         var existingTranscripts = await dbSave.Transcripts
             .Where(t => t.MeetingId == meetingId)
             .ToListAsync();
         foreach (var existingTranscript in existingTranscripts)
         {
             dbSave.Transcripts.Remove(existingTranscript);
         }
         
         var transcript = new Transcript
         {
             MeetingId = meetingId,
             Engine = result.Engine,
             Model = result.Model,
             Language = result.Language,
             FullText = result.FullText,
         };
         foreach (var seg in result.Segments)
         {
             transcript.Segments.Add(new TranscriptSegment
             {
                 TranscriptId = transcript.Id,
                 StartMs = seg.StartMs,
                 EndMs = seg.EndMs,
                 Text = seg.Text,
                 Confidence = seg.Confidence,
             });
         }
         dbSave.Transcripts.Add(transcript);
         await dbSave.SaveChangesAsync();

         UpdateJob(job, "Transcribed", $"{result.Segments.Count} segments");
         await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
         {
             var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
             if (vm != null)
             {
                 vm.Detail.TranscriptText = result.FullText;
                 vm.Detail.StatusMessage = "Transcription complete";
             }
         });
     }

    private async Task RunManualSummarizeAsync(string meetingId, ProcessingJobViewModel job)
    {
        UpdateJob(job, "Summarizing...", "Reading transcript");

        string? transcriptText;
        string language = "en";
        await using (var db = AppDbContextFactory.Create())
        {
            var meeting = await db.Meetings.FindAsync(meetingId);
            if (meeting != null) language = meeting.Language;

            var transcript = await db.Transcripts
                .Where(t => t.MeetingId == meetingId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (transcript == null || string.IsNullOrEmpty(transcript.FullText))
            {
                var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                transcriptText = vm?.Detail.TranscriptText;
            }
            else
            {
                transcriptText = transcript.FullText;
            }
        }

        if (string.IsNullOrEmpty(transcriptText)) return;

        var (provider, modelId) = await AiProviderFactory.CreateFromSettingsAsync();
        if (modelId == null)
        {
            UpdateJob(job, "Failed", "No AI models available");
            return;
        }

        UpdateJob(job, "Summarizing...", $"{provider.ProviderName} / {modelId}");
        var summary = await provider.SummarizeAsync(transcriptText, "meeting_summary", language, modelId, job.Cts.Token);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            await using var db = AppDbContextFactory.Create();
            db.Summaries.Add(new Summary
            {
                MeetingId = meetingId,
                Provider = provider.ProviderName.ToLower(),
                Model = modelId,
                PromptType = "meeting_summary",
                Content = summary,
                Language = language,
            });
            await db.SaveChangesAsync();

            UpdateJob(job, "Summarized");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                if (vm != null)
                {
                    vm.Detail.SummaryText = summary;
                    vm.Detail.StatusMessage = "Summary complete";
                }
            });
        }
    }

    private async Task RunIdentifySpeakersAsync(string meetingId, ProcessingJobViewModel job)
    {
        UpdateJob(job, "Identifying speakers...", "Reading transcript");

        string? transcriptText;
        string language = "en";
        await using (var db = AppDbContextFactory.Create())
        {
            var meeting = await db.Meetings.FindAsync(meetingId);
            if (meeting != null) language = meeting.Language;

            var transcript = await db.Transcripts
                .Where(t => t.MeetingId == meetingId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (transcript == null || string.IsNullOrEmpty(transcript.FullText))
            {
                var vm2 = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                transcriptText = vm2?.Detail.TranscriptText;
            }
            else
            {
                // Use original text (before any speaker ID) if available
                transcriptText = !string.IsNullOrEmpty(transcript.OriginalFullText)
                    ? transcript.OriginalFullText
                    : transcript.FullText;
            }
        }

        if (string.IsNullOrEmpty(transcriptText)) return;

        var (provider, modelId) = await AiProviderFactory.CreateFromSettingsAsync();
        if (modelId == null)
        {
            UpdateJob(job, "Failed", "No AI models available");
            return;
        }

        UpdateJob(job, "Identifying speakers...", $"{provider.ProviderName} / {modelId}");
        var result = await provider.SummarizeAsync(
            transcriptText, "identify_speakers", language, modelId, job.Cts.Token);

        if (!string.IsNullOrWhiteSpace(result))
        {
            await using var db = AppDbContextFactory.Create();
            var transcript = await db.Transcripts
                .Where(t => t.MeetingId == meetingId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            if (transcript != null)
            {
                if (string.IsNullOrEmpty(transcript.OriginalFullText))
                    transcript.OriginalFullText = transcript.FullText;
                transcript.FullText = result;
                await db.SaveChangesAsync();
            }

            UpdateJob(job, "Speakers identified");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var vm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
                if (vm != null)
                {
                    vm.Detail.SpeakersText = result;
                    vm.Detail.StatusMessage = "Speakers identified";
                }
            });
        }
    }

    // --- Recording Saved ---

    private async Task OnRecordingSavedAsync(string meetingId)
    {
        try
        {
            Log.Information("Recording saved, refreshing meetings list for {MeetingId}", meetingId);
            await Meetings.LoadMeetingsCommand.ExecuteAsync(null);

            bool autoTranscribe = true, autoSummarize = true;
            string smartSteps = "tsm";
            try
            {
                await using var db = AppDbContextFactory.Create();
                var atSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "auto_transcribe");
                var asSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "auto_summarize");
                var stepsSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "smart_process_steps");
                autoTranscribe = atSetting?.Value != "false";
                autoSummarize = asSetting?.Value != "false";
                if (stepsSetting != null) smartSteps = stepsSetting.Value;
            }
            catch { }

            if (!autoTranscribe && !autoSummarize) return;

            string title = meetingId;
            var meetingVm = Meetings.Meetings.FirstOrDefault(m => m.Id == meetingId);
            if (meetingVm != null) title = meetingVm.Title;

            // Enqueue in correct order: transcribe -> identify speakers -> summarize
            if (autoTranscribe && smartSteps.Contains('t'))
                EnqueueManualJob(meetingId, title, "transcribe");
            if (smartSteps.Contains('s'))
                EnqueueManualJob(meetingId, title, "identify_speakers");
            if (autoSummarize && smartSteps.Contains('m'))
                EnqueueManualJob(meetingId, title, "summarize");

            IsQueueVisible = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in post-recording pipeline for {MeetingId}", meetingId);
        }
    }

    // --- Helpers ---

    public static void ShowTrayNotification(string title, string message)
    {
        Services.Platform.PlatformServices.Notifications.ShowNotification(title, message);
    }

    private void SetStatus(string status, bool processing = true)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ProcessingStatus = status;
            IsProcessing = processing;
        });
    }

    private void UpdateJob(ProcessingJobViewModel job, string status, string step = "")
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            job.Status = status;
            job.Step = step;
        });
    }

     /// <summary>Detects if transcript already has speaker labels (e.g. "Speaker A:", "John:", "Manager:").</summary>
     private static bool HasSpeakerLabels(string text)
     {
         var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
         int labelCount = 0;
         foreach (var line in lines)
         {
             var colonIdx = line.IndexOf(':');
             // Pattern: short label (1-30 chars) followed by colon, then text
             if (colonIdx > 0 && colonIdx <= 30 && colonIdx < line.Length - 1)
             {
                 var label = line[..colonIdx].Trim();
                 // Label should be words only (no timestamps, no URLs)
                 if (!string.IsNullOrEmpty(label) && !label.Contains("//") && !label.Contains('.'))
                     labelCount++;
             }
             if (labelCount >= 3) return true; // 3+ labeled lines = speakers identified
         }
         return false;
     }

     /// <summary>Checks if a meeting already has a transcript in the database.</summary>
     private static async Task<bool> HasExistingTranscriptAsync(string meetingId)
     {
         try
         {
             await using var db = AppDbContextFactory.Create();
             return await db.Transcripts
                 .Where(t => t.MeetingId == meetingId)
                 .AnyAsync();
         }
         catch (Exception ex)
         {
             Log.Error(ex, "Error checking for existing transcript for {MeetingId}", meetingId);
             return false;
         }
     }

     /// <summary>Determines if an error is transient and worth retrying.</summary>
     private static bool IsRetryableError(Exception ex)
     {
         // Network-related errors are retryable
         if (ex is HttpRequestException) return true;
         
         // Timeout errors are retryable
         if (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)) return true;
         
         // AI provider connection errors
         if (ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)) return true;
         if (ex.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)) return true;
         if (ex.Message.Contains("no route to host", StringComparison.OrdinalIgnoreCase)) return true;
         
         // Rate limiting
         if (ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase)) return true;
         if (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) return true;
         
         // Service unavailable
         if (ex.Message.Contains("503", StringComparison.OrdinalIgnoreCase)) return true;
         if (ex.Message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) return true;
         
         // Database locking (SQLite BusyError)
         if (ex.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)) return true;
         if (ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)) return true;
         
         // Check for Ollama specific errors
         if (ex.Message.Contains("Ollama returned", StringComparison.OrdinalIgnoreCase))
         {
             // Don't retry on 404 (model not found) or 400 (bad request)
             if (ex.Message.Contains("404") || ex.Message.Contains("400")) return false;
             return true;
         }
         
         // Check for OpenAI specific errors  
         if (ex.Message.Contains("API returned", StringComparison.OrdinalIgnoreCase))
         {
             if (ex.Message.Contains("404") || ex.Message.Contains("400")) return false;
             return true;
         }
         
         // Check for Anthropic specific errors
         if (ex.Message.Contains("Anthropic returned", StringComparison.OrdinalIgnoreCase))
         {
             if (ex.Message.Contains("404") || ex.Message.Contains("400")) return false;
             return true;
         }
         
         return false;
     }

[RelayCommand]
    private void NavigateTo(string view)
    {
        CurrentView = view switch
        {
            "recording" => Recording,
            "meetings" => Meetings,
            "settings" => Settings,
            _ => Recording
        };
    }

    [RelayCommand]
    private void QuitApp()
    {
        Log.Information("VoxMemo quitting via Quit button");
        
        // Suppress the tray notification on close
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.PrepareForExit();
            }
        }
        
        // Hide tray icon before quitting
        try
        {
            if (Avalonia.Application.Current != null)
            {
                var trayIcons = Avalonia.Controls.TrayIcon.GetIcons(Avalonia.Application.Current);
                if (trayIcons != null)
                {
                    foreach (var tray in trayIcons)
                    {
                        tray.IsVisible = false;
                    }
                }
            }
        }
        catch { }
        
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2)
        {
            desktop2.Shutdown();
        }
    }
}
