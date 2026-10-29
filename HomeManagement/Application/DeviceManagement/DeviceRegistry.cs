using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// Keeps devices and their actions in the database in sync with the twins devices publish.
/// </summary>
public sealed class DeviceRegistry(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    ILogger<DeviceRegistry> logger)
{
    public async Task RegisterAsync(string deviceId, DeviceTwin twin, CancellationToken token = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var device = await dbContext.Devices.AsTracking()
                         .Include(x => x.Actions)
                         .FirstOrDefaultAsync(x => x.DeviceId == deviceId, token)
                     ?? await AdoptLegacyDeviceAsync(dbContext, deviceId, twin.Name, token);

        if (device is null)
        {
            device = new Device
            {
                Name = await GetUniqueNameAsync(dbContext, twin.Name, deviceId, token),
                DeviceId = deviceId
            };
            dbContext.Devices.Add(device);
            logger.LogInformation("Registered device '{Name}' ({DeviceId}).", device.Name, deviceId);
        }

        await SyncActionsAsync(dbContext, device, twin.Actions, token);
        await dbContext.SaveChangesAsync(token);
    }

    /// <summary>
    /// Devices added before MQTT had their HTTP address as id. They are matched by name so their workflows keep working.
    /// </summary>
    private async Task<Device?> AdoptLegacyDeviceAsync(HomeManagementDbContext dbContext, string deviceId, string name, CancellationToken token)
    {
        var device = await dbContext.Devices.AsTracking()
            .Include(x => x.Actions)
            .FirstOrDefaultAsync(x => x.Name == name && x.DeviceId.Contains("://"), token);
        if (device is not null)
        {
            logger.LogInformation("Device '{Name}' moved from '{Address}' to MQTT id '{DeviceId}'.", name, device.DeviceId, deviceId);
            device.DeviceId = deviceId;
        }

        return device;
    }

    private static async Task<string> GetUniqueNameAsync(HomeManagementDbContext dbContext, string name, string deviceId, CancellationToken token)
    {
        return await dbContext.Devices.AnyAsync(x => x.Name == name, token)
            ? $"{name} ({deviceId})"
            : name;
    }

    private async Task SyncActionsAsync(HomeManagementDbContext dbContext, Device device, IEnumerable<Shared.DeviceAction> twinActions, CancellationToken token)
    {
        var reported = twinActions
            .DistinctBy(x => x.Command, StringComparer.Ordinal)
            .ToDictionary(x => x.Command, StringComparer.Ordinal);

        foreach (var (command, action) in reported)
        {
            var existing = device.Actions.FirstOrDefault(x => x.Command == command);
            if (existing is null)
            {
                device.Actions.Add(new Infrastructure.Database.Entities.DeviceAction
                {
                    Action = action.Action,
                    Command = command,
                    CommandType = action.CommandType
                });
                continue;
            }

            existing.Action = action.Action;
            existing.CommandType = action.CommandType;
        }

        var removed = device.Actions.Where(x => !reported.ContainsKey(x.Command)).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        var removedIds = removed.Where(x => x.Id != 0).Select(x => x.Id).ToList();
        var usedIds = await dbContext.Set<WorkflowStep>()
            .Where(x => x.DeviceActionId != null && removedIds.Contains(x.DeviceActionId.Value))
            .Select(x => x.DeviceActionId!.Value)
            .Concat(dbContext.Set<WorkflowTriggerCondition>()
                .Where(x => x.TriggerSourceActionId != null && removedIds.Contains(x.TriggerSourceActionId.Value))
                .Select(x => x.TriggerSourceActionId!.Value))
            .ToListAsync(token);

        foreach (var action in removed)
        {
            if (usedIds.Contains(action.Id))
            {
                logger.LogWarning("Device '{Name}' no longer reports action '{Action}', but it is kept because workflows use it.", device.Name, action.Action);
                continue;
            }

            device.Actions.Remove(action);
        }
    }
}
