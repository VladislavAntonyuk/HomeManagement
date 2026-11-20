using System.Linq.Expressions;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
using Microsoft.EntityFrameworkCore;
using DeviceInfo = HomeManagement.Shared.DeviceInfo;
using DeviceTopics = HomeManagement.Shared.DeviceTopics;

namespace HomeManagement.Application.DeviceManagement;

public sealed record DeviceOverview(
    Device Device,
    DevicePresence Presence,
    DeviceInfo? Info,
    DateTimeOffset? InfoReceivedAtUtc,
    IReadOnlyList<DeviceMetricDto> Metrics);

public sealed record DeviceStatusSummary(int Total, int Online, int Offline, int NotResponding);

public sealed class DeviceService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    DeviceStateStore stateStore,
    DeviceActionExecutor actionExecutor,
    DeviceHub deviceHub)
{
    /// <summary>
    /// The registered devices with their actions, without their live state.
    /// </summary>
    public async Task<List<Device>> ListAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        return await db.Devices
            .Include(x => x.Actions.OrderBy(a => a.Action))
            .OrderBy(x => x.Name)
            .ToListAsync(token);
    }

    public async Task<List<DeviceOverview>> GetDevicesAsync(CancellationToken token = default)
        => [.. (await ListAsync(token)).Select(GetOverview)];

    /// <summary>
    /// The device with its current presence and info. Cheap: the live state is in memory.
    /// </summary>
    public DeviceOverview GetOverview(Device device)
    {
        var info = stateStore.GetInfo(device.DeviceId);
        return new DeviceOverview(
            device,
            stateStore.GetPresence(device.DeviceId),
            info,
            info is null ? null : stateStore.GetTelemetry(device.DeviceId, DeviceTopics.InfoKey)?.ReceivedAtUtc,
            info is null ? [] : DisplayFormatter.FormatMetrics(info));
    }

    public async Task<DeviceStatusSummary> GetStatusSummaryAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var deviceIds = await db.Devices.Select(x => x.DeviceId).ToListAsync(token);
        var counts = deviceIds.CountBy(x => stateStore.GetPresence(x).Status).ToDictionary();

        return new DeviceStatusSummary(
            deviceIds.Count,
            counts.GetValueOrDefault(DevicePresenceStatus.Online),
            counts.GetValueOrDefault(DevicePresenceStatus.Offline),
            counts.GetValueOrDefault(DevicePresenceStatus.NotResponding));
    }

    public Task<OperationResult> RunActionAsync(int deviceId, int actionId, CancellationToken token = default)
        => RunActionAsync(x => x.Id == deviceId, x => x.Id == actionId, "Device not found.", "Action not found", token);

    public Task<OperationResult> RunActionAsync(string deviceName, string actionName, CancellationToken token = default)
        => RunActionAsync(x => x.Name == deviceName, x => x.Action == actionName, $"Device '{deviceName}' not found.", $"Action '{actionName}' not found", token);

    public async Task<OperationResult> UpdateAsync(int id, string name, string? description, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var device = await db.Devices.AsTracking().FirstOrDefaultAsync(x => x.Id == id, token);
        if (device is null)
        {
            return OperationResult.NotFound("Device not found.");
        }

        name = name.Trim();
        if (await db.Devices.AnyAsync(x => x.Id != id && x.Name == name, token))
        {
            return OperationResult.Failure($"Another device is already called '{name}'.");
        }

        device.Name = name;
        device.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"'{device.Name}' updated.");
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var device = await db.Devices.AsTracking().Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id, token);
        if (device is null)
        {
            return OperationResult.NotFound("Device not found.");
        }

        var usages = await GetWorkflowUsagesAsync(db, device, token);
        if (usages.Count > 0)
        {
            return OperationResult.Failure($"'{device.Name}' is used by workflows: {string.Join(", ", usages)}. Remove it from those workflows first.");
        }

        db.Devices.Remove(device);
        await db.SaveChangesAsync(token);
        await deviceHub.ForgetDeviceAsync(device.DeviceId, token);
        return OperationResult.Success($"Deleted '{device.Name}'.");
    }

    public async Task<List<string>> GetWorkflowUsagesAsync(int id, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var device = await db.Devices.Include(x => x.Actions).FirstOrDefaultAsync(x => x.Id == id, token);
        return device is null ? [] : await GetWorkflowUsagesAsync(db, device, token);
    }

    private static async Task<List<string>> GetWorkflowUsagesAsync(HomeManagementDbContext db, Device device, CancellationToken token)
    {
        var actionIds = device.Actions.Select(x => x.Id).ToList();
        return await db.Workflows
            .Where(w => w.Steps.OfType<DeviceActionStep>().Any(s => actionIds.Contains(s.DeviceActionId))
                        || w.Triggers.OfType<DeviceResultTrigger>().Any(t => actionIds.Contains(t.DeviceActionId))
                        || w.Triggers.OfType<DeviceStatusTrigger>().Any(t => t.DeviceId == device.Id))
            .Select(w => w.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(token);
    }

    private async Task<OperationResult> RunActionAsync(
        Expression<Func<Device, bool>> devicePredicate,
        Func<DeviceAction, bool> actionPredicate,
        string deviceNotFound,
        string actionNotFound,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var device = await db.Devices.Include(x => x.Actions).FirstOrDefaultAsync(devicePredicate, token);
        if (device is null)
        {
            return OperationResult.NotFound(deviceNotFound);
        }

        if (device.Actions.FirstOrDefault(actionPredicate) is not { } action)
        {
            return OperationResult.NotFound($"{actionNotFound} on '{device.Name}'.");
        }

        var result = await actionExecutor.ExecuteAsync(device, action, token);
        return result switch
        {
            ActionSucceeded => OperationResult.Success(result.Message),
            ActionDeviceOffline or ActionValueMissing => OperationResult.Unavailable(result.Message),
            ActionFailed => OperationResult.Failure(result.Message)
        };
    }
}
