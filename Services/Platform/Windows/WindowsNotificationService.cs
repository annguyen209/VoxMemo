using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsNotificationService : INotificationService
{
    public void ShowNotification(string title, string message)
    {
        try
        {
            // Escape single quotes for PowerShell strings
            var safeTitle = title.Replace("'", "''");
            var safeMsg = message.Replace("'", "''");

            // Use System.Windows.Forms.NotifyIcon balloon — works reliably without UWP/AUMID registration
            var script = $@"
Add-Type -AssemblyName System.Windows.Forms
$balloon = New-Object System.Windows.Forms.NotifyIcon
$balloon.Icon = [System.Drawing.SystemIcons]::Information
$balloon.BalloonTipTitle = '{safeTitle}'
$balloon.BalloonTipText = '{safeMsg}'
$balloon.Visible = $true
$balloon.ShowBalloonTip(5000)
Start-Sleep -Seconds 6
$balloon.Dispose()
";
            var scriptPath = Path.Combine(Path.GetTempPath(), "voxmemo_toast.ps1");
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Warning("Toast notification failed: {Error}", ex.Message);
        }
    }
}
