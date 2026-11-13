using System.Linq.Expressions;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using DeviceInfo = HomeManagement.Shared.DeviceInfo;
using DeviceTopics = HomeManagement.Shared.DeviceTopics;

namespace HomeManagement.Application.DeviceManagement;

public sealed record DeviceOverview(
    Device Device,
    DevicePresence? Presence,
    DeviceInfo? Info,
    DateTimeOffset? InfoReceivedAtUtc,
    IReadOnlyList<FormattedMetric> Metrics);

public sealed record DeviceStatusSummary(int Total, int Online, int Offline, int NotResponding);

public sealed class DeviceService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    DeviceStateStore stateStore,
    IDeviceActionExecutor actionExecutor,
    IDeviceHub deviceHub)
{
    public async Task<List<DeviceOverview>> GetDevicesAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var devices = await db.Devices
            .Include(x => x.Actions.OrderBy(a => a.Action))
            .OrderBy(x => x.Name)
            .ToListAsync(token);

        return devices.Select(GetOverview).ToList();
    }

    public DeviceOverview GetOverview(Device device)
    {
        var info = stateStore.GetInfo(device.DeviceId);
        return new DeviceOverview(
            device,
            stateStore.GetPresence(device.DeviceId),
            info,
            info is null ? null : stateStore.GetTelemetry(device.DeviceId, DeviceTopics.InfoKey)?.ReceivedAtUtc,
            info is null ? [] : DeviceInfoFormatter.FormatMetrics(info));
    }

    public async Task<DeviceStatusSummary> GetStatusSummaryAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var presences = (await db.Devices.Select(x => x.DeviceId).ToListAsync(token))
            .Select(stateStore.GetPresence)
            .ToList();

        return new DeviceStatusSummary(
            presences.Count,
            presences.Count(x => x is { IsOnline: true }),
            presences.Count(x => x is { IsOnline: false, IsHeartbeatMissed: false }),
            presences.Count(x => x is { IsHeartbeatMissed: true }));
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

        device.Name = name.Trim();
        device.Description = string.IsNullOrWhiteSpace(description) ? null : description;
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
            .Where(w => w.Steps.Any(s => s.DeviceActionId != null && actionIds.Contains(s.DeviceActionId.Value))
                        || w.TriggerConditions.Any(c => (c.TriggerSourceActionId != null && actionIds.Contains(c.TriggerSourceActionId.Value))
                                                        || c.TriggerDeviceId == device.Id))
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
        return result.IsSuccess ? OperationResult.Success(result.Message) : OperationResult.Failure(result.Message);
    }
}
