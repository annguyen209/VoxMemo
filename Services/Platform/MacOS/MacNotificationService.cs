using System;
using System.Diagnostics;
using Serilog;

namespace VoxMemo.Services.Platform.MacOS;

/// <summary>
/// macOS notifications using osascript (AppleScript).
/// </summary>
public class MacNotificationService : INotificationService
{
    public void ShowNotification(string title, string message)
    {
        try
        {
            var script = $"display notification \"{message}\" with title \"{title}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Log.Debug("osascript notification failed: {Error}", ex.Message);
        }
    }
}
