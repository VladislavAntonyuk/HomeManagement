using HomeManagement.Application.AwayMode;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Shared;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace HomeManagement.Infrastructure.Api;

/// <summary>
/// REST API for clients of the hub: dashboard, devices and their actions, workflows and cameras.
/// </summary>
public static class HomeApi
{
    public static void MapHomeApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(string.Empty)
            .RequireAuthorization(ApiAuthenticationDefaults.PolicyName)
            .WithMetadata(new SkipStatusCodePagesAttribute())
            .WithTags("Home Management API");

        api.MapGet(ApiRoutes.Dashboard, GetDashboardAsync);
        api.MapPut(ApiRoutes.AwayMode, SetAwayModeAsync);
        api.MapGet(ApiRoutes.Devices, GetDevicesAsync);
        api.MapPost(ApiRoutes.DeviceAction, RunDeviceActionAsync);
        api.MapGet(ApiRoutes.Workflows, GetWorkflowsAsync);
        api.MapPost(ApiRoutes.WorkflowRun, RunWorkflowAsync);
        api.MapPut(ApiRoutes.WorkflowEnabled, SetWorkflowEnabledAsync);
        api.MapCameraApi();
    }

    private static async Task<DashboardDto> GetDashboardAsync(
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        AwayModeService awayModeService,
        DeviceStateStore deviceStateStore,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var deviceIds = await db.Devices.Select(x => x.DeviceId).ToListAsync(token);
        var statuses = deviceIds.Select(x => ToStatus(deviceStateStore.GetPresence(x))).ToList();
        var workflows = await db.Workflows
            .Select(x => new { x.IsEnabled, x.LastTriggeredAtUtc })
            .ToListAsync(token);

        return new DashboardDto(
            await awayModeService.IsEnabledAsync(token),
            statuses.Count,
            statuses.Count(x => x == DevicePresenceStatus.Online),
            statuses.Count(x => x == DevicePresenceStatus.Offline),
            statuses.Count(x => x == DevicePresenceStatus.NotResponding),
            workflows.Count,
            workflows.Count(x => x.IsEnabled),
            workflows.Max(x => x.LastTriggeredAtUtc),
            GetHubInfo());
    }

    private static async Task<OperationResultDto> SetAwayModeAsync(
        SetEnabledRequest request,
        AwayModeService awayModeService,
        CancellationToken token)
    {
        await awayModeService.SetEnabledAsync(request.Enabled, token);
        return new OperationResultDto(true, $"Away mode {(request.Enabled ? "enabled" : "disabled")}.");
    }

    private static async Task<List<DeviceDto>> GetDevicesAsync(
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        DeviceStateStore deviceStateStore,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var devices = await db.Devices
            .Include(x => x.Actions)
            .OrderBy(x => x.Name)
            .ToListAsync(token);

        return devices
            .Select(d =>
            {
                var presence = deviceStateStore.GetPresence(d.DeviceId);
                var info = deviceStateStore.GetInfo(d.DeviceId);
                return new DeviceDto(
                    d.Id,
                    d.Name,
                    d.DeviceId,
                    d.Description,
                    ToStatus(presence),
                    presence?.LastSeenUtc,
                    string.IsNullOrEmpty(info?.Address) ? null : info.Address,
                    info is null
                        ? []
                        : DeviceInfoFormatter.FormatMetrics(info)
                            .Select(x => new DeviceMetricDto(x.Name, x.Value, x.Label, x.DisplayValue))
                            .ToList(),
                    d.Actions
                        .OrderBy(x => x.Action)
                        .Select(x => new DeviceActionDto(x.Id, x.Action, x.CommandType))
                        .ToList());
            })
            .ToList();
    }

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> RunDeviceActionAsync(
        int deviceId,
        int actionId,
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        IDeviceActionExecutor deviceActionExecutor,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var device = await db.Devices
            .Include(x => x.Actions)
            .FirstOrDefaultAsync(x => x.Id == deviceId, token);
        if (device is null)
        {
            return TypedResults.NotFound(new OperationResultDto(false, "Device not found."));
        }

        var action = device.Actions.FirstOrDefault(x => x.Id == actionId);
        if (action is null)
        {
            return TypedResults.NotFound(new OperationResultDto(false, $"Action not found on '{device.Name}'."));
        }

        var result = await deviceActionExecutor.ExecuteAsync(device, action, token);
        return TypedResults.Ok(new OperationResultDto(result.IsSuccess, result.Message));
    }

    private static async Task<List<WorkflowDto>> GetWorkflowsAsync(
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflows = await db.Workflows
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerSourceAction)
            .ThenInclude(x => x!.Device)
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerDevice)
            .Include(x => x.Steps)
            .OrderBy(x => x.Name)
            .ToListAsync(token);

        return workflows
            .Select(w => new WorkflowDto(
                w.Id,
                w.Name,
                w.Description,
                w.IsEnabled,
                w.RepeatWhileMatched,
                WorkflowConditionFormatter.FormatTrigger(w),
                w.Steps.Count,
                w.LastTriggeredAtUtc))
            .ToList();
    }

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> RunWorkflowAsync(
        int workflowId,
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        IWorkflowRunner workflowRunner,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows.AsTracking().FirstOrDefaultAsync(x => x.Id == workflowId, token);
        if (workflow is null)
        {
            return TypedResults.NotFound(new OperationResultDto(false, "Workflow not found."));
        }

        var result = await workflowRunner.RunAsync(workflow, token: token);
        workflow.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return TypedResults.Ok(result.IsSuccessful
            ? new OperationResultDto(true, $"'{workflow.Name}' executed, {result.ExecutedActions} step(s) run.")
            : new OperationResultDto(false, $"'{workflow.Name}' executed with errors: {string.Join(" | ", result.Errors)}"));
    }

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> SetWorkflowEnabledAsync(
        int workflowId,
        SetEnabledRequest request,
        IDbContextFactory<HomeManagementDbContext> dbContextFactory,
        CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows.AsTracking().FirstOrDefaultAsync(x => x.Id == workflowId, token);
        if (workflow is null)
        {
            return TypedResults.NotFound(new OperationResultDto(false, "Workflow not found."));
        }

        workflow.IsEnabled = request.Enabled;
        await db.SaveChangesAsync(token);
        return TypedResults.Ok(new OperationResultDto(true, $"'{workflow.Name}' {(request.Enabled ? "enabled" : "disabled")}."));
    }

    private static DevicePresenceStatus ToStatus(DevicePresence? presence) => presence switch
    {
        { IsHeartbeatMissed: true } => DevicePresenceStatus.NotResponding,
        { IsOnline: true } => DevicePresenceStatus.Online,
        { IsOnline: false } => DevicePresenceStatus.Offline,
        _ => DevicePresenceStatus.Unknown
    };

    private static HubInfoDto GetHubInfo()
    {
        string? localIp;
        try
        {
            localIp = NetworkManager.GetLocalIp();
        }
        catch
        {
            localIp = null;
        }

        var temperature = OperatingSystem.IsLinux() ? DeviceManager.GetTemperature() : 0;
        return new HubInfoDto(
            Environment.MachineName,
            localIp,
            RuntimeInformation.OSDescription,
            Environment.TickCount64 / 1000,
            temperature > 0 ? Math.Round(temperature, 1) : null);
    }
}
