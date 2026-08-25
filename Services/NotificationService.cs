namespace BackupManager.Services;

public class NotificationEventArgs : EventArgs
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
}

public interface INotificationService
{
    event EventHandler<NotificationEventArgs>? NotificationRequested;
    void Show(string title, string message, bool isError = false);
}

public class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public void Show(string title, string message, bool isError = false)
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs
        {
            Title = title,
            Message = message,
            IsError = isError
        });
    }
}
