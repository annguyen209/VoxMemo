using System;
using System.Diagnostics;
using Serilog;

namespace VoxMemo.Services.Platform.Linux;

/// <summary>
/// Linux notifications using notify-send (libnotify).
/// </summary>
public class LinuxNotificationService : INotificationService
{
    public void ShowNotification(string title, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notify-send",
                Arguments = $"--app-name=VoxMemo \"{title}\" \"{message}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Log.Debug("notify-send failed: {Error}", ex.Message);
        }
    }
}
