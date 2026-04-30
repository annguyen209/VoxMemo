using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Serilog;
using VoxMemo.Services.Database;
using VoxMemo.ViewModels;
using VoxMemo.Views;

namespace VoxMemo;

public partial class App : Application
{
    private MainWindowViewModel? _mainVm;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        Log.Information("Initializing database");
        
        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoxMemo", "voxmemo.db");
        
        await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            
            // Create tables if not exist
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Meetings (Id TEXT PRIMARY KEY, Title TEXT, Platform TEXT, StartedAt TEXT, EndedAt TEXT, AudioPath TEXT, DurationMs INTEGER, Language TEXT, CreatedAt TEXT);
                    CREATE TABLE IF NOT EXISTS Transcripts (Id TEXT PRIMARY KEY, MeetingId TEXT, Engine TEXT, Model TEXT, Language TEXT, FullText TEXT, OriginalFullText TEXT, CreatedAt TEXT);
                    CREATE TABLE IF NOT EXISTS TranscriptSegments (Id TEXT PRIMARY KEY, TranscriptId TEXT, StartMs INTEGER, EndMs INTEGER, Text TEXT, Confidence REAL);
                    CREATE TABLE IF NOT EXISTS Summaries (Id TEXT PRIMARY KEY, MeetingId TEXT, Provider TEXT, Model TEXT, PromptType TEXT, Content TEXT, Language TEXT, CreatedAt TEXT);
                    CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT, EncryptedValue TEXT);
                ";
                await cmd.ExecuteNonQueryAsync();
            }
            
            // Add OriginalFullText column if missing
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM pragma_table_info('Transcripts') WHERE name='OriginalFullText'";
                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                {
                    await using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = "ALTER TABLE Transcripts ADD COLUMN OriginalFullText TEXT";
                    await cmd2.ExecuteNonQueryAsync();
                }
            }
            
            // Add EncryptedValue column if missing
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM pragma_table_info('AppSettings') WHERE name='EncryptedValue'";
                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                {
                    await using var cmd2 = conn.CreateCommand();
                    cmd2.CommandText = "ALTER TABLE AppSettings ADD COLUMN EncryptedValue TEXT";
                    await cmd2.ExecuteNonQueryAsync();
                }
            }
        }

        // Apply saved theme
        string savedTheme = "dark";
        try
        {
            await using var themeDb = AppDbContextFactory.Create();
            var themeSetting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(themeDb.AppSettings, s => s.Key == "ui_theme");
            if (themeSetting != null && !string.IsNullOrEmpty(themeSetting.Value))
                savedTheme = themeSetting.Value;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load theme setting, defaulting to dark");
        }
        SetTheme(savedTheme);

        // Check if first-run onboarding is needed
        bool needsOnboarding = false;
        try
        {
            await using var onbDb = AppDbContextFactory.Create();
            var onbSetting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(onbDb.AppSettings, s => s.Key == "onboarding_complete");
            needsOnboarding = onbSetting?.Value != "true";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check onboarding_complete, skipping wizard");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Information("Setting up main window");
            DisableAvaloniaDataAnnotationValidation();
            _mainVm = new MainWindowViewModel();
            var mainWindow = new MainWindow { DataContext = _mainVm };
            desktop.MainWindow = mainWindow;

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (needsOnboarding)
            {
                mainWindow.IsVisible = false;
                var onboarding = new VoxMemo.Views.OnboardingWindow();
                onboarding.Closed += (_, _) => mainWindow.IsVisible = true;
                onboarding.Show();
            }

            // Only rebuild when tray-visible state actually changes.
            _mainVm.Recording.PropertyChanged += (_, e) =>
            {
                if (ShouldRebuildTrayMenu(e.PropertyName))
                    RebuildTrayMenu();
            };

            // Rebuild when enabled languages change
            SettingsViewModel.EnabledLanguagesChanged += (_, _) => RebuildTrayMenu();

            // Build initial menu
            RebuildTrayMenu();

            // Register global hotkey
            _ = RegisterHotkeyFromSettingsAsync();
            SettingsViewModel.HotkeyChanged += (_, newHotkey) => RegisterHotkey(newHotkey);

            desktop.ShutdownRequested += (_, _) =>
            {
                if (desktop.MainWindow is MainWindow mw)
                    mw.PrepareForExit();
                Services.Platform.PlatformServices.GlobalHotkey.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void SetTheme(string theme)
    {
        RequestedThemeVariant = theme == "light"
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }

    /// <summary>
    /// Rebuilds the tray menu when recording state or selections change.
    /// </summary>
    private void RebuildTrayMenu()
    {
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons == null || trayIcons.Count == 0 || _mainVm == null) return;

        var tray = trayIcons[0];
        var rec = _mainVm.Recording;
        var isRecording = rec.IsRecording;

        // Update tooltip
        tray.ToolTipText = isRecording
            ? (rec.IsPaused ? "VoxMemo - Paused" : "VoxMemo - Recording...")
            : "VoxMemo - Meeting Recorder";

        var menu = new NativeMenu();

        // Show VoxMemo
        var showItem = new NativeMenuItem { Header = "Show VoxMemo" };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        // Audio Source submenu
        var audioSourceSub = new NativeMenu();
        foreach (var source in rec.AudioSources)
        {
            var isSelected = source == rec.SelectedAudioSource;
            var item = new NativeMenuItem
            {
                Header = isSelected ? $"> {source}" : $"   {source}",
                IsEnabled = !isRecording
            };
            var s = source;
            item.Click += (_, _) => { rec.SelectedAudioSource = s; };
            audioSourceSub.Items.Add(item);
        }
        menu.Items.Add(new NativeMenuItem { Header = "Audio Source", Menu = audioSourceSub, IsEnabled = !isRecording });

        // Device submenu
        var deviceSub = new NativeMenu();
        foreach (var device in rec.Devices)
        {
            var isSelected = device == rec.SelectedDevice;
            var item = new NativeMenuItem
            {
                Header = isSelected ? $"> {device.Name}" : $"   {device.Name}",
                IsEnabled = !isRecording
            };
            var d = device;
            item.Click += (_, _) => { rec.SelectedDevice = d; };
            deviceSub.Items.Add(item);
        }
        if (deviceSub.Items.Count == 0)
            deviceSub.Items.Add(new NativeMenuItem { Header = "(no devices)", IsEnabled = false });
        menu.Items.Add(new NativeMenuItem { Header = "Device", Menu = deviceSub, IsEnabled = !isRecording });

        // Language submenu
        var langSub = new NativeMenu();
        foreach (var lang in rec.Languages)
        {
            var isSelected = lang == rec.SelectedLanguage;
            var item = new NativeMenuItem
            {
                Header = isSelected ? $"> {lang}" : $"   {lang}",
                IsEnabled = !isRecording
            };
            var l = lang;
            item.Click += (_, _) => { rec.SelectedLanguage = l; };
            langSub.Items.Add(item);
        }
        menu.Items.Add(new NativeMenuItem { Header = "Language", Menu = langSub, IsEnabled = !isRecording });

        menu.Items.Add(new NativeMenuItemSeparator());

        // Recording controls
        if (!isRecording)
        {
            var startItem = new NativeMenuItem { Header = "Start Recording (Ctrl+Shift+R)" };
            startItem.Click += async (_, _) =>
            {
                if (rec.StartRecordingCommand.CanExecute(null))
                    await rec.StartRecordingCommand.ExecuteAsync(null);
            };
            menu.Items.Add(startItem);
        }
        else
        {
            var pauseItem = new NativeMenuItem { Header = rec.IsPaused ? "Resume" : "Pause" };
            pauseItem.Click += (_, _) =>
            {
                if (rec.PauseRecordingCommand.CanExecute(null))
                    rec.PauseRecordingCommand.Execute(null);
            };
            menu.Items.Add(pauseItem);

            var stopItem = new NativeMenuItem { Header = "Stop Recording (Ctrl+Shift+R)" };
            stopItem.Click += async (_, _) =>
            {
                if (rec.StopRecordingCommand.CanExecute(null))
                    await rec.StopRecordingCommand.ExecuteAsync(null);
            };
            menu.Items.Add(stopItem);
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        // Exit
        var exitItem = new NativeMenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            {
                if (d.MainWindow is MainWindow mainWindow)
                    mainWindow.PrepareForExit();
                d.Shutdown();
            }
        };
        menu.Items.Add(exitItem);

        tray.Menu = menu;
    }

    private static bool ShouldRebuildTrayMenu(string? propertyName) => propertyName is
        nameof(RecordingViewModel.IsRecording) or
        nameof(RecordingViewModel.IsPaused) or
        nameof(RecordingViewModel.SelectedAudioSource) or
        nameof(RecordingViewModel.SelectedDevice) or
        nameof(RecordingViewModel.SelectedLanguage);

    // Show notification on recording state change
    private void OnRecordingStateChanged()
    {
        if (_mainVm == null) return;
        var rec = _mainVm.Recording;
        if (rec.IsRecording && !rec.IsPaused)
        {
            MainWindowViewModel.ShowTrayNotification("VoxMemo", "Recording started (Ctrl+Shift+R to stop)");
        }
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null)
            {
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            }
        }
    }

    // --- Global Hotkey ---

    private async Task RegisterHotkeyFromSettingsAsync()
    {
        string hotkey = "Ctrl+Shift+R";
        try
        {
            await using var db = AppDbContextFactory.Create();
            var setting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(db.AppSettings, s => s.Key == "recording_hotkey");
            if (setting != null && !string.IsNullOrEmpty(setting.Value))
                hotkey = setting.Value;
        }
        catch { }

        RegisterHotkey(hotkey);
    }

    private void RegisterHotkey(string hotkeyStr)
    {
        var hotkey = Services.Platform.PlatformServices.GlobalHotkey;
        hotkey.Dispose(); // unregister previous

        var (modifiers, vk) = ParseHotkey(hotkeyStr);
        if (vk == 0)
        {
            Log.Warning("Invalid hotkey: {Hotkey}", hotkeyStr);
            return;
        }

        hotkey.Register(modifiers, vk, OnToggleRecordingHotkey);
        Log.Information("Global hotkey registered: {Hotkey}", hotkeyStr);
    }

    private static (int modifiers, int vk) ParseHotkey(string hotkey)
    {
        int modifiers = 0;
        int vk = 0;

        var parts = hotkey.Split('+').Select(p => p.Trim().ToLower()).ToArray();
        foreach (var part in parts)
        {
            switch (part)
            {
                case "ctrl" or "control": modifiers |= Services.Platform.IGlobalHotkeyService.MOD_CONTROL; break;
                case "shift": modifiers |= Services.Platform.IGlobalHotkeyService.MOD_SHIFT; break;
                case "alt": modifiers |= Services.Platform.IGlobalHotkeyService.MOD_ALT; break;
                case "win": modifiers |= Services.Platform.IGlobalHotkeyService.MOD_WIN; break;
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                        vk = char.ToUpper(part[0]);
                    else if (part.StartsWith("f") && int.TryParse(part[1..], out var fnum) && fnum is >= 1 and <= 24)
                        vk = 0x70 + fnum - 1;
                    else if (part == "space") vk = 0x20;
                    break;
            }
        }

        return (modifiers, vk);
    }

    private void OnToggleRecordingHotkey()
    {
        if (_mainVm == null) return;

        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            var rec = _mainVm.Recording;
            if (rec.IsRecording)
            {
                if (rec.StopRecordingCommand.CanExecute(null))
                    await rec.StopRecordingCommand.ExecuteAsync(null);
            }
            else
            {
                if (rec.StartRecordingCommand.CanExecute(null))
                    await rec.StartRecordingCommand.ExecuteAsync(null);
            }
        });
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
