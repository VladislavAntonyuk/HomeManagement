using HomeManagement.Infrastructure.Database;
using System.Threading.Channels;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateEventsSubscriber(
    Channel<string> channel,
    ILogger<FrigateEventsSubscriber> logger,
    HomeManagementDbContext dbContext)
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

        var awayModeSetting = await dbContext.AppSettings
            .AsNoTracking()
            .Select(x => new AppSetting
            {
                Id = x.Id,
                Name = x.Name,
                Value = x.Value,
                ValueType = x.ValueType
            })
            .FirstOrDefaultAsync(x => x.Name == AppSettingKeys.AwayModeEnabled);

        var isAwayModeEnabled = awayModeSetting?.TryGetBoolean(out var awayModeEnabled) == true && awayModeEnabled;
        if (!isAwayModeEnabled)
        {
            logger.LogInformation("Skipping Frigate event because away mode is disabled.");
            return;
        }

        var snapshotUrl = after.HasSnapshot
            ? $"http://cameras.home-management.local/api/events/{after.Id}/snapshot.jpg?bbox=1"
            : $"http://cameras.home-management.local/api/{after.Camera}/latest.jpg";

        var alertMessage = $"Person detected on camera '{after.Camera}' (Score: {after.TopScore:P0}, Event ID: {after.Id}), Snapshot URL: {snapshotUrl}";

        await channel.Writer.WriteAsync(alertMessage);
        logger.LogInformation("Successfully dispatched alert to Channel: {Alert}", alertMessage);
    }
}