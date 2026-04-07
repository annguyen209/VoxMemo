using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxMemo.Models;
using VoxMemo.Services.AI;
using VoxMemo.Services.Database;
using System.IO;
using VoxMemo.Services.Transcription;
using Microsoft.EntityFrameworkCore;

namespace VoxMemo.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    // Platform awareness
    public bool IsGlobalHotkeySupported { get; } =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

    public bool IsStartupSupported { get; } =
        Services.Platform.PlatformServices.Startup?.IsSupported ?? false;

    public string StartupLabel { get; } =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "Start VoxMemo when Windows starts"
            : "Start VoxMemo at login";

    // AI Provider Settings
    [ObservableProperty]
    private string _selectedAiProvider = "Ollama";

    [ObservableProperty]
    private string _ollamaUrl = "http://localhost:11434";

    [ObservableProperty]
    private string _openAiApiKey = string.Empty;

    [ObservableProperty]
    private string _openAiBaseUrl = "https://api.openai.com/v1";

    [ObservableProperty]
    private string _anthropicApiKey = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AiModel> _availableAiModels = [];

    [ObservableProperty]
    private AiModel? _selectedAiModel;

    // Transcription Settings
    [ObservableProperty]
    private string _selectedWhisperModel = "base";

    [ObservableProperty]
    private ObservableCollection<string> _availableWhisperModels = [];

    [ObservableProperty]
    private ObservableCollection<string> _downloadableWhisperModels =
        ["tiny", "base", "small", "medium", "large"];

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private string _downloadProgress = string.Empty;

    // Automation Settings
    [ObservableProperty]
    private bool _autoTranscribe = true;

    [ObservableProperty]
    private bool _autoSummarize = true;

    [ObservableProperty]
    private bool _smartProcessSkipDialog;

    [ObservableProperty]
    private string _recordingHotkey = "Ctrl+Shift+R";

    [ObservableProperty]
    private string _customSummaryPrompt = string.Empty;

    [ObservableProperty]
    private string _customSpeakerPrompt = string.Empty;

    // Prompt templates
    public ObservableCollection<PromptTemplate> SummaryTemplates { get; } =
    [
        new("(Default - built-in)", ""),
        new("Action Items Focus", "You are a project manager. Summarize this meeting focusing only on action items, deadlines, and who is responsible. Ignore small talk and pleasantries. Format as a numbered list of action items with owner and due date."),
        new("Executive Brief", "You are an executive assistant. Write a brief 3-5 sentence executive summary of this meeting. Focus on decisions made, key outcomes, and next steps. Be concise and professional."),
        new("Technical Review", "You are a senior engineer. Summarize this technical meeting focusing on: technical decisions made, architecture changes discussed, bugs or issues raised, and code review outcomes. Use technical terminology where appropriate."),
        new("Sales/Client Call", "You are a sales operations analyst. Summarize this client call focusing on: client needs and pain points, proposed solutions, pricing discussed, objections raised, and agreed next steps."),
        new("Interview Notes", "You are an HR coordinator. Summarize this interview focusing on: candidate qualifications discussed, strengths and concerns noted, culture fit observations, and hiring recommendation if mentioned."),
        new("Standup/Scrum", "You are a scrum master. Summarize this standup meeting per person: what they completed, what they're working on, and any blockers mentioned. Keep it brief and structured."),
        new("Brainstorm Session", "You are a product manager. Summarize this brainstorming session: list all ideas proposed, group them by theme, note which ideas had the most support, and any decisions on next steps."),
    ];

    public ObservableCollection<PromptTemplate> SpeakerTemplates { get; } =
    [
        new("(Default - built-in)", ""),
        new("By Name", "Reformat this transcript as a dialog. Identify speakers by their real names if mentioned anywhere in the conversation. Each speaker turn on a new line as 'Name: words'. Add blank line between speakers."),
        new("By Role", "Reformat this transcript as a dialog using professional roles (e.g. Manager, Developer, Designer, Client). Infer roles from context. Each speaker turn as 'Role: words'. Add blank line between speakers."),
        new("Interview Format", "Reformat this transcript as an interview dialog between Interviewer and Candidate. If multiple interviewers, label them as Interviewer 1, Interviewer 2. Preserve all words exactly."),
        new("Teacher/Student", "Reformat this transcript as a dialog between Teacher and Student(s). If multiple students, label them as Student 1, Student 2, etc. Preserve all words exactly."),
        new("Minimal Labels", "Reformat this transcript with minimal speaker labels: Person 1, Person 2, etc. Split based on sentence boundaries, question-answer patterns, and topic shifts. Add blank line between speakers."),
    ];

    [ObservableProperty]
    private PromptTemplate? _selectedSummaryTemplate;

    [ObservableProperty]
    private PromptTemplate? _selectedSpeakerTemplate;

    private bool _templateInitDone;

    partial void OnSelectedSummaryTemplateChanged(PromptTemplate? value)
    {
        if (value != null && _templateInitDone)
            CustomSummaryPrompt = value.Prompt;
    }

    partial void OnSelectedSpeakerTemplateChanged(PromptTemplate? value)
    {
        if (value != null && _templateInitDone)
            CustomSpeakerPrompt = value.Prompt;
    }

    [ObservableProperty]
    private bool _startWithWindows;

    // General Settings
    public ObservableCollection<Models.LanguageItem> AllLanguages { get; } = new(Models.WhisperLanguages.All);

    [ObservableProperty]
    private ObservableCollection<Models.LanguageItem> _enabledLanguages = new(
        Models.WhisperLanguages.All.FindAll(l => l.Code is "en" or "vi"));

    [ObservableProperty]
    private ObservableCollection<string> _enabledLanguageCodes = ["en", "vi"];

    [ObservableProperty]
    private string _defaultLanguage = "en";

    /// <summary>Raised when enabled languages change so RecordingViewModel can update.</summary>
    public static event System.EventHandler<List<string>>? EnabledLanguagesChanged;

    [ObservableProperty]
    private string _storagePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> AiProviders { get; } = ["Ollama", "OpenAI", "Anthropic"];

    private bool _isLoading;

    public SettingsViewModel()
    {
        _ = LoadSettingsAsync();
    }

    partial void OnSelectedAiProviderChanged(string value) => SaveIfNotLoading();
    partial void OnOllamaUrlChanged(string value) => SaveIfNotLoading();
    partial void OnOpenAiApiKeyChanged(string value) => SaveIfNotLoading();
    partial void OnOpenAiBaseUrlChanged(string value) => SaveIfNotLoading();
    partial void OnAnthropicApiKeyChanged(string value) => SaveIfNotLoading();
    partial void OnSelectedWhisperModelChanged(string value) => SaveIfNotLoading();
    partial void OnAutoTranscribeChanged(bool value) => SaveIfNotLoading();
    partial void OnAutoSummarizeChanged(bool value) => SaveIfNotLoading();
    partial void OnSmartProcessSkipDialogChanged(bool value) => SaveIfNotLoading();
    partial void OnCustomSummaryPromptChanged(string value) => SaveIfNotLoading();
    partial void OnCustomSpeakerPromptChanged(string value) => SaveIfNotLoading();
    partial void OnRecordingHotkeyChanged(string value)
    {
        if (!_isLoading)
        {
            _ = SaveSettingsAsync();
            HotkeyChanged?.Invoke(this, value);
        }
    }
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_isLoading) Services.Platform.PlatformServices.Startup.SetStartupEnabled(value);
    }
    partial void OnDefaultLanguageChanged(string value) => SaveIfNotLoading();
    partial void OnStoragePathChanged(string value) => SaveIfNotLoading();

    private void SaveIfNotLoading()
    {
        if (!_isLoading) _ = SaveSettingsAsync();
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        _isLoading = true;
        try
        {
            await using var db = new AppDbContext();

            SelectedAiProvider = await GetSettingAsync(db, "ai_provider", "Ollama");
            OllamaUrl = await GetSettingAsync(db, "ollama_url", "http://localhost:11434");
            OpenAiApiKey = await GetSettingAsync(db, "openai_api_key", string.Empty);
            OpenAiBaseUrl = await GetSettingAsync(db, "openai_base_url", "https://api.openai.com/v1");
            AnthropicApiKey = await GetSettingAsync(db, "anthropic_api_key", string.Empty);
            SelectedWhisperModel = await GetSettingAsync(db, "whisper_model", "base");
            AutoTranscribe = (await GetSettingAsync(db, "auto_transcribe", "true")) == "true";
            AutoSummarize = (await GetSettingAsync(db, "auto_summarize", "true")) == "true";
            SmartProcessSkipDialog = (await GetSettingAsync(db, "smart_process_skip_dialog", "false")) == "true";
            RecordingHotkey = await GetSettingAsync(db, "recording_hotkey", "Ctrl+Shift+R");
            CustomSummaryPrompt = await GetSettingAsync(db, "custom_summary_prompt", string.Empty);
            CustomSpeakerPrompt = await GetSettingAsync(db, "custom_speaker_prompt", string.Empty);
            StartWithWindows = Services.Platform.PlatformServices.Startup.IsStartupEnabled();
            var langCodes = await GetSettingAsync(db, "enabled_languages", "en,vi");
            EnabledLanguages = new ObservableCollection<Models.LanguageItem>(
                Models.WhisperLanguages.All.FindAll(l => langCodes.Split(',').Contains(l.Code)));
            SyncLanguageCodes();
            DefaultLanguage = await GetSettingAsync(db, "default_language", "en");
            StoragePath = await GetSettingAsync(db, "storage_path",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxMemo"));

            var whisperService = new WhisperTranscriptionService();
            var models = await whisperService.GetAvailableModelsAsync();
            AvailableWhisperModels = new ObservableCollection<string>(models);

            await RefreshAiModelsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
            _templateInitDone = true;
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await using var db = new AppDbContext();

            await SetSettingAsync(db, "ai_provider", SelectedAiProvider);
            await SetSettingAsync(db, "ollama_url", OllamaUrl);
            await SetSettingAsync(db, "openai_api_key", OpenAiApiKey);
            await SetSettingAsync(db, "openai_base_url", OpenAiBaseUrl);
            await SetSettingAsync(db, "anthropic_api_key", AnthropicApiKey);
            await SetSettingAsync(db, "whisper_model", SelectedWhisperModel);
            await SetSettingAsync(db, "auto_transcribe", AutoTranscribe ? "true" : "false");
            await SetSettingAsync(db, "auto_summarize", AutoSummarize ? "true" : "false");
            await SetSettingAsync(db, "smart_process_skip_dialog", SmartProcessSkipDialog ? "true" : "false");
            await SetSettingAsync(db, "recording_hotkey", RecordingHotkey);
            await SetSettingAsync(db, "custom_summary_prompt", CustomSummaryPrompt);
            await SetSettingAsync(db, "custom_speaker_prompt", CustomSpeakerPrompt);
            await SetSettingAsync(db, "enabled_languages", string.Join(",", EnabledLanguages.Select(l => l.Code)));
            await SetSettingAsync(db, "default_language", DefaultLanguage);
            await SetSettingAsync(db, "storage_path", StoragePath);

            await db.SaveChangesAsync();
        }
        catch { }
    }

    private static async Task<string> GetSettingAsync(AppDbContext db, string key, string defaultValue)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    private static async Task SetSettingAsync(AppDbContext db, string key, string value)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
        }
        else
        {
            db.AppSettings.Add(new AppSettings { Key = key, Value = value });
        }
    }

    [RelayCommand]
    private async Task DownloadWhisperModelAsync(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return;

        IsDownloadingModel = true;
        DownloadProgress = $"Downloading {modelName} model...";

        try
        {
            var service = new WhisperTranscriptionService();
            await service.DownloadModelAsync(modelName, new Progress<float>(bytes =>
            {
                DownloadProgress = $"Downloading {modelName}: {bytes / 1024 / 1024:F1} MB";
            }));

            var models = await service.GetAvailableModelsAsync();
            AvailableWhisperModels = new ObservableCollection<string>(models);
            SelectedWhisperModel = modelName;
            StatusMessage = $"Model '{modelName}' downloaded successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingModel = false;
            DownloadProgress = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RefreshAiModelsAsync()
    {
        try
        {
            IAiProvider provider = SelectedAiProvider switch
            {
                "Ollama" => new OllamaProvider(OllamaUrl),
                "OpenAI" => new OpenAiProvider(OpenAiApiKey, OpenAiBaseUrl),
                "Anthropic" when !string.IsNullOrEmpty(AnthropicApiKey) => new AnthropicProvider(AnthropicApiKey),
                _ => new OllamaProvider(OllamaUrl)
            };

            var models = await provider.GetAvailableModelsAsync();
            AvailableAiModels = new ObservableCollection<AiModel>(models);

            if (models.Count > 0)
                SelectedAiModel = models[0];

            StatusMessage = $"Found {models.Count} models for {SelectedAiProvider}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading models: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenModelsFolder()
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo", "models");
        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private async Task ImportModelAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Whisper ggml model",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Whisper model")
                    {
                        Patterns = ["*.bin"]
                    }
                ]
            });

        if (files.Count == 0) return;

        var sourcePath = files[0].Path.LocalPath;
        var fileName = System.IO.Path.GetFileName(sourcePath);

        // Ensure ggml- prefix
        if (!fileName.StartsWith("ggml-"))
            fileName = $"ggml-{fileName}";
        if (!fileName.EndsWith(".bin"))
            fileName += ".bin";

        var modelsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo", "models");
        System.IO.Directory.CreateDirectory(modelsDir);

        var destPath = System.IO.Path.Combine(modelsDir, fileName);
        System.IO.File.Copy(sourcePath, destPath, true);

        // Refresh list
        var service = new WhisperTranscriptionService();
        var models = await service.GetAvailableModelsAsync();
        AvailableWhisperModels = new ObservableCollection<string>(models);

        var modelName = fileName.Replace("ggml-", "").Replace(".bin", "");
        SelectedWhisperModel = modelName;
        StatusMessage = $"Model '{modelName}' imported successfully";
    }

    [RelayCommand]
    private async Task RefreshWhisperModelsAsync()
    {
        var service = new WhisperTranscriptionService();
        var models = await service.GetAvailableModelsAsync();
        AvailableWhisperModels = new ObservableCollection<string>(models);
        StatusMessage = $"Found {models.Count} Whisper models";
    }

    [ObservableProperty]
    private Models.LanguageItem? _selectedAvailableLanguage;

    private void SyncLanguageCodes()
    {
        EnabledLanguageCodes = new ObservableCollection<string>(EnabledLanguages.Select(l => l.Code));
    }

    [RelayCommand]
    private void AddLanguage()
    {
        if (SelectedAvailableLanguage == null) return;
        if (EnabledLanguages.Any(l => l.Code == SelectedAvailableLanguage.Code)) return;
        EnabledLanguages.Add(SelectedAvailableLanguage);
        SyncLanguageCodes();
        _ = SaveSettingsAsync();
        EnabledLanguagesChanged?.Invoke(this, EnabledLanguages.Select(l => l.Code).ToList());
    }

    [RelayCommand]
    private void RemoveLanguage(Models.LanguageItem? lang)
    {
        if (lang == null || EnabledLanguages.Count <= 1) return;
        EnabledLanguages.Remove(lang);
        SyncLanguageCodes();
        if (DefaultLanguage == lang.Code && EnabledLanguages.Count > 0)
            DefaultLanguage = EnabledLanguages[0].Code;
        _ = SaveSettingsAsync();
        EnabledLanguagesChanged?.Invoke(this, EnabledLanguages.Select(l => l.Code).ToList());
    }

    /// <summary>Raised when the hotkey setting changes so App can re-register.</summary>
    public static event EventHandler<string>? HotkeyChanged;

    [RelayCommand]
    private async Task TestOllamaConnectionAsync()
    {
        try
        {
            var provider = new OllamaProvider(OllamaUrl);
            var models = await provider.GetAvailableModelsAsync();
            StatusMessage = models.Count > 0
                ? $"Connected! Found {models.Count} models."
                : "Connected but no models found. Run 'ollama pull <model>' to download one.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
    }

}

public record PromptTemplate(string Name, string Prompt)
{
    public override string ToString() => Name;
}
