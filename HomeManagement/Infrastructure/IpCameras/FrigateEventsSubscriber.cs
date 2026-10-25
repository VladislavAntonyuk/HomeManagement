using HomeManagement.Application.AwayMode;
using HomeManagement.Application.WebHooks;

namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateEventsSubscriber(
    IEnumerable<ISender> senders,
    SenderRequestFactory senderRequestFactory,
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
            ? $"http://cameras.home-management.local/api/events/{after.Id}/snapshot.jpg?bbox=1"
            : $"http://cameras.home-management.local/api/{after.Camera}/latest.jpg";

        var alertMessage = $"Person detected on camera '{after.Camera}' (Score: {after.TopScore:P0}, Event ID: {after.Id}), Snapshot URL: {snapshotUrl}";

        foreach (var sender in senders)
        {
            var request = senderRequestFactory.Create(alertMessage, sender);
            if (request is null)
            {
                continue;
            }

            var sendResult = await sender.Send(request, CancellationToken.None);
            if (!sendResult.IsSuccessful)
            {
                logger.LogError(sendResult.Error);
            }
        }
    }
}
