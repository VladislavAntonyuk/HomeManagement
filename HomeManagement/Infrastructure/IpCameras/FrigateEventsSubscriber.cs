using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Threading.Channels;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateEventsSubscriber(
    Channel<string> channel,
    ILogger<FrigateEventsSubscriber> logger,
    IDbContextFactory<HomeManagementDbContext> dbContextFactory)
{
    private readonly ConcurrentDictionary<string, DateTime> _seenEvents = new();

    public async Task OnFrigateEventAsync(FrigateEventMessage message)
    {
        if (message.Type is not "new")
        {
            return;
        }

        var after = message.After;
        if (after is null || after.Label != "person" || string.IsNullOrEmpty(after.Id))
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
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

        if (!_seenEvents.TryAdd(after.Id, DateTime.UtcNow))
        {
            return;
        }

        var expiry = DateTime.UtcNow.AddMinutes(-10);
        foreach (var (key, time) in _seenEvents)
        {
            if (time < expiry)
            {
                _seenEvents.TryRemove(key, out _);
            }
        }

        var snapshotUrl = after.HasSnapshot
            ? $"http://cameras.home-management.local/api/events/{after.Id}/snapshot.jpg?bbox=1"
            : $"http://cameras.home-management.local/api/{after.Camera}/latest.jpg";

        await channel.Writer.WriteAsync($"Person detected on camera '{after.Camera}' (Score: {after.TopScore:P0}, Event ID: {after.Id}), Snapshot URL: {snapshotUrl}");
    }
}