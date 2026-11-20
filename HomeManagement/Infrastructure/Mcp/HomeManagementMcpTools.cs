using HomeManagement.Application;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Shared;
using HomeManagement.Infrastructure.Api;
using HomeManagement.Shared.Api;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace HomeManagement.Infrastructure.Mcp;

public record McpDeviceAction(string Name, string CommandType);

/// <param name="Name">Metric name as the device reports it, e.g. UptimeSeconds.</param>
/// <param name="Value">Raw value.</param>
/// <param name="Label">Readable name, e.g. Uptime.</param>
/// <param name="DisplayValue">Readable value, e.g. 1 d 2 h.</param>
public record McpDeviceInfo(string? Address, IReadOnlyList<DeviceMetricDto> Metrics, DateTimeOffset UpdatedAtUtc);

/// <param name="Status">online, offline, not responding (reports online but stopped sending heartbeats) or unknown.</param>
/// <param name="LastSeenUtc">When the hub last received a message of the device.</param>
public record McpDevice(
    string Name,
    string DeviceId,
    string Status,
    DateTimeOffset? LastSeenUtc,
    string? Description,
    McpDeviceInfo? Info,
    IReadOnlyList<McpDeviceAction> Actions);

public record McpHomeDashboard(IReadOnlyList<McpDevice> Devices, IReadOnlyList<WorkflowDto> Workflows);

/// <param name="Message">Raw result, e.g. the JSON the device replied with.</param>
/// <param name="DisplayMessage">Readable result, e.g. the device JSON flattened to "key: value" lines.</param>
public record McpOperationResult(bool IsSuccess, string Message, string DisplayMessage)
{
    public McpOperationResult(bool isSuccess, string message)
        : this(isSuccess, message, DisplayFormatter.FormatActionResult(message))
    {
    }
}

[McpServerToolType]
public class HomeManagementMcpTools
{
    public const string DashboardUri = "ui://home-management/dashboard.html";

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [McpAppUi(ResourceUri = DashboardUri)]
    [Description("Shows the home dashboard with devices and their actions, and workflows. The user can run actions and workflows from it.")]
    public static async Task<McpHomeDashboard> ShowHomeDashboard(
        DeviceService deviceService,
        WorkflowService workflowService,
        CancellationToken token)
    {
        return new McpHomeDashboard(
            await GetDevicesAsync(deviceService, token),
            await GetWorkflowsAsync(workflowService, token));
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [Description("Lists all devices with their status, when they were last seen, their info (address, uptime, signal and other metrics) and their actions")]
    public static Task<List<McpDevice>> ListDevices(DeviceService deviceService, CancellationToken token)
        => GetDevicesAsync(deviceService, token);

    [McpServerTool(UseStructuredContent = true)]
    [Description("Runs an action of a device, both found by name")]
    public static async Task<McpOperationResult> RunAction(
        [Description("Device name")] string deviceName,
        [Description("Action name")] string action,
        DeviceService deviceService,
        CancellationToken token)
        => ToMcp(await deviceService.RunActionAsync(deviceName, action, token));

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [Description("Lists all workflows with their triggers")]
    public static Task<List<WorkflowDto>> ListWorkflows(WorkflowService workflowService, CancellationToken token)
        => GetWorkflowsAsync(workflowService, token);

    [McpServerTool(UseStructuredContent = true)]
    [Description("Runs a workflow by name now, regardless of its trigger conditions")]
    public static async Task<McpOperationResult> RunWorkflow(
        [Description("Workflow name")] string name,
        WorkflowService workflowService,
        CancellationToken token)
        => ToMcp(await workflowService.RunAsync(name, token));

    [McpServerTool(UseStructuredContent = true, Idempotent = true)]
    [Description("Enables or disables a workflow by name")]
    public static async Task<McpOperationResult> SetWorkflowEnabled(
        [Description("Workflow name")] string name,
        [Description("true to enable, false to disable")] bool enabled,
        WorkflowService workflowService,
        CancellationToken token)
        => ToMcp(await workflowService.SetEnabledAsync(name, enabled, token));

    private static McpOperationResult ToMcp(OperationResult result) => new(result.IsSuccess, result.Message);

    private static async Task<List<McpDevice>> GetDevicesAsync(DeviceService deviceService, CancellationToken token)
        => (await deviceService.GetDevicesAsync(token))
            .Select(x => new McpDevice(
                x.Device.Name,
                x.Device.DeviceId,
                x.Presence.Status.DisplayName.ToLowerInvariant(),
                x.Presence.LastSeenUtc,
                x.Device.Description,
                x.Info is null || x.InfoReceivedAtUtc is null
                    ? null
                    : new McpDeviceInfo(
                        x.Address,
                        x.Metrics,
                        x.InfoReceivedAtUtc.Value),
                x.Device.Actions.Select(a => new McpDeviceAction(a.Action, a.CommandType.ToString())).ToList()))
            .ToList();

    private static async Task<List<WorkflowDto>> GetWorkflowsAsync(WorkflowService workflowService, CancellationToken token)
        => [.. (await workflowService.GetWorkflowsAsync(token)).Select(x => x.ToDto())];
}
