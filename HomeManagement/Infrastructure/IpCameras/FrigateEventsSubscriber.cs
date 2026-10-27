using HomeManagement.Application.AwayMode;
using HomeManagement.Application.Notifications;

namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateEventsSubscriber(
    INotificationSender notificationSender,
    ILogger<FrigateEventsSubscriber> logger,
    AwayModeService awayModeService)
{
    public async Task OnFrigateEventAsync(FrigateEventMessage message)
    {
        logger.LogInformation("Received Frigate event: {EventType} {EventLabel}", message.Type, message.After?.Label);
        if (message.Type is not "new")
        {
            return;
        }

        var after = message.After;
        if (after is null || after.Label != "person" || string.IsNullOrEmpty(after.Id))
        {
            return;
        }

        if (!await awayModeService.IsEnabledAsync())
        {
            logger.LogInformation("Skipping Frigate event because away mode is disabled.");
            return;
        }

        var snapshotUrl = after.HasSnapshot
            ? $"http://{AppHosts.CamerasHost}/api/events/{after.Id}/snapshot.jpg?bbox=1"
            : $"http://{AppHosts.CamerasHost}/api/{after.Camera}/latest.jpg";

        var alertMessage = $"Person detected on camera '{after.Camera}' (Score: {after.TopScore:P0}, Event ID: {after.Id}), Snapshot URL: {snapshotUrl}";

        var sendResult = await notificationSender.SendAsync(alertMessage, CancellationToken.None);
        if (!sendResult.IsSuccessful)
        {
            logger.LogError("Failed to send Frigate alert: {Error}", sendResult.Error);
        }
    }
}
