using System;
using System.Diagnostics;
using Serilog;

namespace VoxMemo.Services.Platform.Windows;

public class WindowsNotificationService : INotificationService
{
    public void ShowNotification(string title, string message)
    {
        try
        {
            var escapedTitle = title.Replace("'", "''");
            var escapedMsg = message.Replace("'", "''");
            var script = $@"
                [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;
                [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime] | Out-Null;
                $xml = '<toast><visual><binding template=""ToastGeneric""><text>{escapedTitle}</text><text>{escapedMsg}</text></binding></visual></toast>';
                $doc = New-Object Windows.Data.Xml.Dom.XmlDocument;
                $doc.LoadXml($xml);
                $toast = [Windows.UI.Notifications.ToastNotification]::new($doc);
                [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('VoxMemo').Show($toast);
            ";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Debug("Toast notification failed: {Error}", ex.Message);
        }
    }
}
