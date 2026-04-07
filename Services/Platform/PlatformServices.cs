using System.Runtime.InteropServices;
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

    /// <summary>Creates a new playback instance (each meeting player needs its own).</summary>
    public static IAudioPlaybackService CreatePlaybackService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new Windows.WindowsAudioPlaybackService();
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
