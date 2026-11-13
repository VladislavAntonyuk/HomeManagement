using HomeManagement.Application;
using HomeManagement.Application.AwayMode;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Shared;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;

namespace HomeManagement.Infrastructure.Api;

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
        DeviceService deviceService,
        WorkflowService workflowService,
        AwayModeService awayModeService,
        CancellationToken token)
    {
        var devices = await deviceService.GetStatusSummaryAsync(token);
        var workflows = await workflowService.GetSummaryAsync(token);
        return new DashboardDto(
            await awayModeService.IsEnabledAsync(token),
            devices.Total,
            devices.Online,
            devices.Offline,
            devices.NotResponding,
            workflows.Total,
            workflows.Enabled,
            workflows.LastRunAtUtc,
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

    private static async Task<List<DeviceDto>> GetDevicesAsync(DeviceService deviceService, CancellationToken token)
        => (await deviceService.GetDevicesAsync(token)).Select(ToDto).ToList();

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> RunDeviceActionAsync(
        int deviceId,
        int actionId,
        DeviceService deviceService,
        CancellationToken token)
        => ToHttpResult(await deviceService.RunActionAsync(deviceId, actionId, token));

    private static async Task<List<WorkflowDto>> GetWorkflowsAsync(WorkflowService workflowService, CancellationToken token)
        => (await workflowService.GetWorkflowsAsync(token))
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

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> RunWorkflowAsync(
        int workflowId,
        WorkflowService workflowService,
        CancellationToken token)
        => ToHttpResult(await workflowService.RunAsync(workflowId, token));

    private static async Task<Results<Ok<OperationResultDto>, NotFound<OperationResultDto>>> SetWorkflowEnabledAsync(
        int workflowId,
        SetEnabledRequest request,
        WorkflowService workflowService,
        CancellationToken token)
        => ToHttpResult(await workflowService.SetEnabledAsync(workflowId, request.Enabled, token));

    private static Results<Ok<OperationResultDto>, NotFound<OperationResultDto>> ToHttpResult(OperationResult result)
    {
        var dto = new OperationResultDto(result.IsSuccess, result.Message);
        return result.Status == OperationStatus.NotFound ? TypedResults.NotFound(dto) : TypedResults.Ok(dto);
    }

    private static DeviceDto ToDto(DeviceOverview overview)
    {
        var device = overview.Device;
        return new DeviceDto(
            device.Id,
            device.Name,
            device.DeviceId,
            device.Description,
            ToStatus(overview.Presence),
            overview.Presence?.LastSeenUtc,
            string.IsNullOrEmpty(overview.Info?.Address) ? null : overview.Info.Address,
            overview.Metrics.Select(x => new DeviceMetricDto(x.Name, x.Value, x.Label, x.DisplayValue)).ToList(),
            device.Actions.Select(x => new DeviceActionDto(x.Id, x.Action, x.CommandType)).ToList());
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
