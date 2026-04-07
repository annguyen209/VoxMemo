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
        await using (var db = new AppDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Information("Setting up main window");
            DisableAvaloniaDataAnnotationValidation();
            _mainVm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainVm,
            };

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Rebuild tray menu on any recording state change
            _mainVm.Recording.PropertyChanged += (_, _) => RebuildTrayMenu();

            // Rebuild when enabled languages change
            SettingsViewModel.EnabledLanguagesChanged += (_, _) => RebuildTrayMenu();

            // Build initial menu
            RebuildTrayMenu();

            // Register global hotkey
            _ = RegisterHotkeyFromSettingsAsync();
            SettingsViewModel.HotkeyChanged += (_, newHotkey) => RegisterHotkey(newHotkey);

            desktop.ShutdownRequested += (_, _) =>
            {
                Services.Platform.PlatformServices.GlobalHotkey.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Rebuilds the entire tray menu from current ViewModel state.
    /// Called on every property change — simple, always consistent.
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
                d.Shutdown();
        };
        menu.Items.Add(exitItem);

        tray.Menu = menu;
    }

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
            await using var db = new AppDbContext();
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
