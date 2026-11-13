using HomeManagement.Application;
using HomeManagement.Application.AwayMode;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace HomeManagement.Infrastructure.Mcp;

public record McpDeviceAction(string Name, string CommandType);

/// <param name="Name">Metric name as the device reports it, e.g. UptimeSeconds.</param>
/// <param name="Value">Raw value.</param>
/// <param name="Label">Readable name, e.g. Uptime.</param>
/// <param name="DisplayValue">Readable value, e.g. 1 d 2 h.</param>
public record McpDeviceMetric(string Name, string Value, string Label, string DisplayValue);

public record McpDeviceInfo(string? Address, IReadOnlyList<McpDeviceMetric> Metrics, DateTimeOffset UpdatedAtUtc);

/// <param name="Status">online, offline, not responding (reports online but stopped sending heartbeats) or unknown.</param>
/// <param name="LastSeenUtc">When the hub last received a message of the device.</param>
public record McpDevice(
    string Name,
    string DeviceId,
    bool? IsOnline,
    string Status,
    DateTimeOffset? LastSeenUtc,
    string? Description,
    McpDeviceInfo? Info,
    IReadOnlyList<McpDeviceAction> Actions);

public record McpWorkflow(
    string Name,
    string? Description,
    bool IsEnabled,
    bool RepeatWhileMatched,
    string Trigger,
    int Steps,
    DateTimeOffset? LastTriggeredAtUtc);

public record McpHomeDashboard(bool AwayModeEnabled, IReadOnlyList<McpDevice> Devices, IReadOnlyList<McpWorkflow> Workflows);

public record McpOperationResult(bool IsSuccess, string Message);

[McpServerToolType]
public class HomeManagementMcpTools
{
    public const string DashboardUri = "ui://home-management/dashboard.html";

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [McpAppUi(ResourceUri = DashboardUri)]
    [Description("Shows the home dashboard with away mode, devices and their actions, and workflows. The user can run actions and workflows from it.")]
    public static async Task<McpHomeDashboard> ShowHomeDashboard(
        DeviceService deviceService,
        WorkflowService workflowService,
        AwayModeService awayModeService,
        CancellationToken token)
    {
        return new McpHomeDashboard(
            await awayModeService.IsEnabledAsync(token),
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
    public static Task<List<McpWorkflow>> ListWorkflows(WorkflowService workflowService, CancellationToken token)
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

    [McpServerTool(UseStructuredContent = true, Idempotent = true)]
    [Description("Turns away mode on or off. In away mode cameras alert on detected people.")]
    public static async Task<McpOperationResult> SetAwayMode(
        [Description("true to turn away mode on, false to turn it off")] bool enabled,
        AwayModeService awayModeService,
        CancellationToken token)
    {
        await awayModeService.SetEnabledAsync(enabled, token);
        return new McpOperationResult(true, $"Away mode {(enabled ? "enabled" : "disabled")}.");
    }

    private static McpOperationResult ToMcp(OperationResult result) => new(result.IsSuccess, result.Message);

    private static async Task<List<McpDevice>> GetDevicesAsync(DeviceService deviceService, CancellationToken token)
        => (await deviceService.GetDevicesAsync(token))
            .Select(x => new McpDevice(
                x.Device.Name,
                x.Device.DeviceId,
                x.Presence?.IsOnline,
                DeviceInfoFormatter.FormatStatus(x.Presence).ToLowerInvariant(),
                x.Presence?.LastSeenUtc,
                x.Device.Description,
                x.Info is null || x.InfoReceivedAtUtc is null
                    ? null
                    : new McpDeviceInfo(
                        string.IsNullOrEmpty(x.Info.Address) ? null : x.Info.Address,
                        x.Metrics.Select(m => new McpDeviceMetric(m.Name, m.Value, m.Label, m.DisplayValue)).ToList(),
                        x.InfoReceivedAtUtc.Value),
                x.Device.Actions.Select(a => new McpDeviceAction(a.Action, a.CommandType.ToString())).ToList()))
            .ToList();

    private static async Task<List<McpWorkflow>> GetWorkflowsAsync(WorkflowService workflowService, CancellationToken token)
        => (await workflowService.GetWorkflowsAsync(token))
            .Select(w => new McpWorkflow(
                w.Name,
                w.Description,
                w.IsEnabled,
                w.RepeatWhileMatched,
                WorkflowConditionFormatter.FormatTrigger(w),
                w.Steps.Count,
                w.LastTriggeredAtUtc))
            .ToList();
}
