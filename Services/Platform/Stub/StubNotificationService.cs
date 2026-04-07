using Serilog;

namespace VoxMemo.Services.Platform.Stub;

public class StubNotificationService : INotificationService
{
    public void ShowNotification(string title, string message)
    {
        Log.Information("[Notification] {Title}: {Message}", title, message);
    }
}
