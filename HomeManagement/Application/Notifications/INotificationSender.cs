namespace HomeManagement.Application.Notifications;

public interface INotificationSender
{
    Task<OperationResult> SendAsync(string message, CancellationToken token);
}
