using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoxMemo.Models;
using VoxMemo.Services.AI;
using VoxMemo.Services.Database;
using VoxMemo.Services.Platform;
using VoxMemo.Services.Security;
using VoxMemo.Services.Transcription;

namespace VoxMemo.ViewModels;

public record WhisperModelOption(string Name, string DisplaySize, bool IsRecommended);

public partial class OnboardingViewModel : ViewModelBase
{
    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _selectedAiProvider = "Ollama";
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _ollamaStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadProgress = string.Empty;
    [ObservableProperty] private bool _modelDownloaded;
    [ObservableProperty] private string _downloadedModelName = string.Empty;
    [ObservableProperty] private AudioDeviceItem? _selectedDevice;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<AudioDeviceItem> AvailableDevices { get; } = [];

    public WhisperModelOption[] DownloadableModels { get; } =
    [
        new("base",  "142 MB", IsRecommended: true),
        new("tiny",  "75 MB",  IsRecommended: false),
        new("small", "466 MB", IsRecommended: false),
    ];

    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool ShowApiKey => SelectedAiProvider is "OpenAI" or "Anthropic";
    public bool CanGoNext => !IsDownloading;

    public event EventHandler? CloseRequested;

    internal void RaiseCloseForTest() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        if (value == 1) _ = CheckOllamaAsync();
        if (value == 3) LoadDevices();
    }

    partial void OnSelectedAiProviderChanged(string value) =>
        OnPropertyChanged(nameof(ShowApiKey));

    partial void OnIsDownloadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanGoNext));

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CurrentStep < 3)
            CurrentStep++;
        else
            await FinishAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0) CurrentStep--;
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        await SaveAndCloseAsync();
    }

    [RelayCommand]
    private async Task DownloadModelAsync(string modelName)
    {
        if (IsDownloading) return;
        IsDownloading = true;
        DownloadProgress = $"Downloading {modelName}...";
        try
        {
            var service = new WhisperTranscriptionService();
            await service.DownloadModelAsync(modelName, new Progress<float>(mb =>
            {
                DownloadProgress = $"Downloading {modelName}… {mb / 1_048_576f:F1} MB";
            }));
            DownloadedModelName = modelName;
            ModelDownloaded = true;
            DownloadProgress = $"{modelName} ready";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Model download failed in onboarding");
            DownloadProgress = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private System.Threading.CancellationTokenSource? _ollamaCts;

    [RelayCommand]
    private async Task CheckOllamaAsync()
    {
        _ollamaCts?.Cancel();
        _ollamaCts = new System.Threading.CancellationTokenSource();
        var ct = _ollamaCts.Token;
        OllamaStatus = "Checking...";
        try
        {
            var provider = new OllamaProvider("http://localhost:11434");
            var models = await provider.GetAvailableModelsAsync();
            if (ct.IsCancellationRequested) return;
            OllamaStatus = models.Count > 0
                ? $"✓ Detected at localhost:11434 ({models.Count} models)"
                : "✓ Connected — no models yet (run 'ollama pull llama3')";
        }
        catch
        {
            if (!ct.IsCancellationRequested)
                OllamaStatus = "Not running — install Ollama or choose another provider";
        }
    }

    private void LoadDevices()
    {
        try
        {
            var devices = new System.Collections.Generic.List<AudioDeviceItem>();
            devices.Add(new AudioDeviceItem(null, "Auto (Default)", false));
            try
            {
                foreach (var d in PlatformServices.AudioRecorder.GetInputDevices())
                    devices.Add(new AudioDeviceItem(d.Id, d.Name, d.IsLoopback));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to enumerate audio devices in onboarding");
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AvailableDevices.Clear();
                foreach (var d in devices) AvailableDevices.Add(d);
                SelectedDevice = AvailableDevices[0];
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load devices in onboarding");
        }
    }

    private async Task FinishAsync()
    {
        try
        {
            await using var db = AppDbContextFactory.Create();
            await UpsertAsync(db, "ai_provider", SelectedAiProvider);
            if (SelectedAiProvider == "OpenAI" && !string.IsNullOrEmpty(ApiKey))
                await SaveApiKeyAsync(db, "openai_api_key", ApiKey);
            else if (SelectedAiProvider == "Anthropic" && !string.IsNullOrEmpty(ApiKey))
                await SaveApiKeyAsync(db, "anthropic_api_key", ApiKey);
            if (!string.IsNullOrEmpty(DownloadedModelName))
                await UpsertAsync(db, "whisper_model", DownloadedModelName);
            if (SelectedDevice?.Id != null)
                await UpsertAsync(db, "audio_input_device", SelectedDevice.Id);
            await UpsertAsync(db, "onboarding_complete", "true");
            await db.SaveChangesAsync();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save onboarding settings");
            ErrorMessage = "Failed to save settings. Please try again.";
        }
    }

    private async Task SaveAndCloseAsync()
    {
        try
        {
            await using var db = AppDbContextFactory.Create();
            await UpsertAsync(db, "onboarding_complete", "true");
            await db.SaveChangesAsync();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mark onboarding complete on skip");
            ErrorMessage = "Failed to save settings. Please try again.";
        }
    }

    private static async Task UpsertAsync(AppDbContext db, string key, string value)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null) setting.Value = value;
        else db.AppSettings.Add(new AppSettings { Key = key, Value = value });
    }

    private static async Task SaveApiKeyAsync(AppDbContext db, string key, string plaintext)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) { setting = new AppSettings { Key = key }; db.AppSettings.Add(setting); }
        setting.EncryptedValue = SecureStorage.Encrypt(plaintext);
        setting.Value = "";
    }
}
