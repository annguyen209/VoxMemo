using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using VoxMemo.Services.Audio;

namespace VoxMemo.Services.Platform;

/// <summary>
/// Central factory for platform-specific services.
/// Call Initialize() once at startup. All consumers access services via static properties.
/// </summary>
public static class PlatformServices
{
    public static IAudioRecorder AudioRecorder { get; private set; } = null!;
    public static IAudioConverter AudioConverter { get; private set; } = null!;
    public static INotificationService Notifications { get; private set; } = null!;
    public static IGlobalHotkeyService GlobalHotkey { get; private set; } = null!;
    public static IStartupService Startup { get; private set; } = null!;

    /// <summary>Warning message if critical dependencies are missing. Null if all OK.</summary>
    public static string? DependencyWarning { get; private set; }

    /// <summary>Checks that required external tools are available on the current platform.</summary>
    public static void ValidateDependencies()
    {
        var missing = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!IsCommandAvailable("ffmpeg")) missing.Add("ffmpeg");
            if (!IsCommandAvailable("ffprobe")) missing.Add("ffprobe");
            if (!IsCommandAvailable("ffplay")) missing.Add("ffplay");
            if (!IsCommandAvailable("parecord")) missing.Add("parecord (pulseaudio-utils)");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (!IsCommandAvailable("ffmpeg")) missing.Add("ffmpeg");
            if (!IsCommandAvailable("ffprobe")) missing.Add("ffprobe");
        }

        if (missing.Count > 0)
        {
            var installHint = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Install with: sudo apt install ffmpeg pulseaudio-utils (Debian/Ubuntu) or equivalent for your distro."
                : "Install with: brew install ffmpeg";

            DependencyWarning = $"Missing: {string.Join(", ", missing)}. {installHint}";
            Log.Warning("Missing dependencies: {Missing}. {Hint}", string.Join(", ", missing), installHint);
        }
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Creates a new playback instance (each meeting player needs its own).</summary>
    public static IAudioPlaybackService CreatePlaybackService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new Windows.WindowsAudioPlaybackService();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOS.MacAudioPlaybackService();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new Linux.LinuxAudioPlaybackService();
        return new Stub.StubAudioPlaybackService();
    }

    public static void Initialize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AudioRecorder = new Windows.WindowsAudioRecorderService();
            AudioConverter = new Windows.WindowsAudioConverter();
            Notifications = new Windows.WindowsNotificationService();
            GlobalHotkey = new Windows.WindowsGlobalHotkeyService();
            Startup = new Windows.WindowsStartupService();
            Windows.WindowsRecordingRecoveryService.RecoverInterruptedRecordings();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            AudioRecorder = new Linux.LinuxAudioRecorderService();
            AudioConverter = new Linux.LinuxAudioConverter();
            Notifications = new Linux.LinuxNotificationService();
            GlobalHotkey = new Stub.StubGlobalHotkeyService(); // no cross-desktop hotkey API
            Startup = new Linux.LinuxStartupService();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            AudioRecorder = new MacOS.MacAudioRecorderService();
            AudioConverter = new MacOS.MacAudioConverter();
            Notifications = new MacOS.MacNotificationService();
            GlobalHotkey = new Stub.StubGlobalHotkeyService(); // requires Carbon/AppKit
            Startup = new MacOS.MacStartupService();
        }
        else
        {
            AudioRecorder = new Stub.StubAudioRecorderService();
            AudioConverter = new Stub.StubAudioConverter();
            Notifications = new Stub.StubNotificationService();
            GlobalHotkey = new Stub.StubGlobalHotkeyService();
            Startup = new Stub.StubStartupService();
        }
    }
}
