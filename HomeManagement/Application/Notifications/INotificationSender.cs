namespace HomeManagement.Application.Notifications;

public interface INotificationSender
{
    Task<HasErrorResult> SendAsync(string message, CancellationToken token);
}
