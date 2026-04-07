using System;
using System.Diagnostics;
using System.IO;

namespace VoxMemo.Services.Platform.Linux;

/// <summary>
/// Linux auto-start using XDG autostart spec (~/.config/autostart/).
/// </summary>
public class LinuxStartupService : IStartupService
{
    private static readonly string AutostartDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart");

    private static readonly string DesktopFilePath = Path.Combine(AutostartDir, "voxmemo.desktop");

    public bool IsSupported => true;

    public bool IsStartupEnabled() => File.Exists(DesktopFilePath);

    public void SetStartupEnabled(bool enable)
    {
        if (enable)
        {
            Directory.CreateDirectory(AutostartDir);

            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "VoxMemo";
            var content = $"""
                [Desktop Entry]
                Type=Application
                Name=VoxMemo
                Comment=Meeting Recorder
                Exec={exePath}
                Terminal=false
                Categories=AudioVideo;Audio;
                """;

            File.WriteAllText(DesktopFilePath, content);
        }
        else
        {
            if (File.Exists(DesktopFilePath))
                File.Delete(DesktopFilePath);
        }
    }
}
