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
            // Sanitize for XML
            var safeTitle = System.Security.SecurityElement.Escape(title);
            var safeMsg = System.Security.SecurityElement.Escape(message);

            // Write a temp PS1 script to avoid quote escaping hell
            var scriptPath = Path.Combine(Path.GetTempPath(), "voxmemo_toast.ps1");
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null
$xml = '<toast><visual><binding template=""ToastGeneric""><text>{safeTitle}</text><text>{safeMsg}</text></binding></visual></toast>'
$doc = New-Object Windows.Data.Xml.Dom.XmlDocument
$doc.LoadXml($xml)
$toast = [Windows.UI.Notifications.ToastNotification]::new($doc)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('VoxMemo').Show($toast)
";
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
