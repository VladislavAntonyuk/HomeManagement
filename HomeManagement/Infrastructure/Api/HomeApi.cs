using HomeManagement.Application;
using HomeManagement.Application.Dashboard;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Cameras;
using HomeManagement.DeviceSdk;
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
        api.MapGet(ApiRoutes.DashboardWidgets, GetDashboardWidgetsAsync);
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
        CancellationToken token)
    {
        var devices = await deviceService.GetStatusSummaryAsync(token);
        var workflows = await workflowService.GetSummaryAsync(token);
        return new DashboardDto(
            devices.Total,
            devices.Online,
            devices.Offline,
            devices.NotResponding,
            workflows.Total,
            workflows.Enabled,
            workflows.LastRunAtUtc,
            GetHubInfo());
    }

    private static async Task<List<DashboardWidgetDto>> GetDashboardWidgetsAsync(DashboardWidgetService widgetService, CancellationToken token)
        => [.. (await widgetService.ListAsync(token)).Select(x => x.ToDto(widgetService.Read(x)))];

    private static async Task<List<DeviceDto>> GetDevicesAsync(DeviceService deviceService, CancellationToken token)
        => [.. (await deviceService.GetDevicesAsync(token)).Select(x => x.ToDto())];

    private static async Task<Results<Ok<OperationResultDto>, ProblemHttpResult>> RunDeviceActionAsync(
        int deviceId,
        int actionId,
        DeviceService deviceService,
        CancellationToken token)
        => ToHttpResult(await deviceService.RunActionAsync(deviceId, actionId, token));

    private static async Task<List<WorkflowDto>> GetWorkflowsAsync(WorkflowService workflowService, CancellationToken token)
        => [.. (await workflowService.GetWorkflowsAsync(token)).Select(x => x.ToDto())];

    private static async Task<Results<Ok<OperationResultDto>, ProblemHttpResult>> RunWorkflowAsync(
        int workflowId,
        WorkflowService workflowService,
        CancellationToken token)
        => ToHttpResult(await workflowService.RunAsync(workflowId, token));

    private static async Task<Results<Ok<OperationResultDto>, ProblemHttpResult>> SetWorkflowEnabledAsync(
        int workflowId,
        SetEnabledRequest request,
        WorkflowService workflowService,
        CancellationToken token)
        => ToHttpResult(await workflowService.SetEnabledAsync(workflowId, request.Enabled, token));

    private static Results<Ok<OperationResultDto>, ProblemHttpResult> ToHttpResult(OperationResult result)
        => result.Status switch
        {
            OperationStatus.Succeeded => TypedResults.Ok(result.ToDto()),
            OperationStatus.NotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            OperationStatus.Unavailable => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status502BadGateway)
        };

    private static HubInfoDto GetHubInfo()
    {
        return new HubInfoDto(
            Environment.MachineName,
            HostInfo.LocalIpAddress,
            RuntimeInformation.OSDescription,
            (long)HostInfo.Uptime.TotalSeconds,
            HostInfo.CpuTemperature is { } temperature ? Math.Round(temperature, 1) : null);
    }
}
