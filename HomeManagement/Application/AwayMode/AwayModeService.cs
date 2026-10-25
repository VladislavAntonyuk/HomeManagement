using HomeManagement.Infrastructure;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Infrastructure.IpCameras;
using Microsoft.EntityFrameworkCore;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Application.AwayMode;

public sealed class AwayModeService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IPublisher publisher,
    ILogger<AwayModeService> logger)
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var setting = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == AppSettingKeys.AwayModeEnabled, cancellationToken);

        return setting?.TryGetBoolean(out var isEnabled) == true && isEnabled;
    }

    public async Task SetEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default)
    {
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var existing = await db.AppSettings.FirstOrDefaultAsync(x => x.Name == AppSettingKeys.AwayModeEnabled, cancellationToken);
            if (existing is null)
            {
                var setting = new AppSetting
                {
                    Name = AppSettingKeys.AwayModeEnabled,
                    Value = string.Empty
                };
                setting.SetBoolean(isEnabled);
                db.AppSettings.Add(setting);
            }
            else
            {
                existing.SetBoolean(isEnabled);
                db.AppSettings.Update(existing);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var profile = isEnabled ? FrigateProfile.Away : FrigateProfile.Default;
        await publisher.PublishAsync(new StringMessage<FrigateProfile>(profile), cancellationToken);
        logger.LogInformation("Away mode set to {IsEnabled}, Frigate profile switched to '{Profile}'.", isEnabled, profile);
    }
}
