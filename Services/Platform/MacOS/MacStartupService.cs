using System;
using System.Diagnostics;
using System.IO;

namespace VoxMemo.Services.Platform.MacOS;

/// <summary>
/// macOS auto-start using LaunchAgent plist.
/// </summary>
public class MacStartupService : IStartupService
{
    private static readonly string LaunchAgentsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents");

    private static readonly string PlistPath = Path.Combine(LaunchAgentsDir, "com.voxmemo.app.plist");

    public bool IsSupported => true;

    public bool IsStartupEnabled() => File.Exists(PlistPath);

    public void SetStartupEnabled(bool enable)
    {
        if (enable)
        {
            Directory.CreateDirectory(LaunchAgentsDir);

            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "VoxMemo";
            var plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.voxmemo.app</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;

            File.WriteAllText(PlistPath, plist);
        }
        else
        {
            if (File.Exists(PlistPath))
                File.Delete(PlistPath);
        }
    }
}
