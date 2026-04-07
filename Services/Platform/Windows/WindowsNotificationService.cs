using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsNotificationService : INotificationService
{
    private const string AppUserModelId = "Anzdev4life.VoxMemo";

    public void ShowNotification(string title, string message)
    {
        try
        {
            var safeTitle = System.Security.SecurityElement.Escape(title);
            var safeMsg = System.Security.SecurityElement.Escape(message);
            // Escape single quotes for PowerShell strings
            var psTitle = title.Replace("'", "''");
            var psMsg = message.Replace("'", "''");

            // Try WinRT toast first (works when installed with AUMID shortcut),
            // fall back to NotifyIcon balloon (works everywhere)
            var script = $@"
try {{
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null
    $xml = '<toast><visual><binding template=""ToastGeneric""><text>{safeTitle}</text><text>{safeMsg}</text></binding></visual></toast>'
    $doc = New-Object Windows.Data.Xml.Dom.XmlDocument
    $doc.LoadXml($xml)
    $toast = [Windows.UI.Notifications.ToastNotification]::new($doc)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{AppUserModelId}').Show($toast)
}} catch {{
    Add-Type -AssemblyName System.Windows.Forms
    $balloon = New-Object System.Windows.Forms.NotifyIcon
    $balloon.Icon = [System.Drawing.SystemIcons]::Information
    $balloon.BalloonTipTitle = '{psTitle}'
    $balloon.BalloonTipText = '{psMsg}'
    $balloon.Visible = $true
    $balloon.ShowBalloonTip(5000)
    Start-Sleep -Seconds 6
    $balloon.Dispose()
}}
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
