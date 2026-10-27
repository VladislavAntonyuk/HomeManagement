using HomeManagement.Application.AwayMode;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace HomeManagement.Infrastructure.Mcp;

public record McpDeviceAction(string Name, string CommandType);

public record McpDevice(string Name, string Address, string? Description, IReadOnlyList<McpDeviceAction> Actions);

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
        HomeManagementDbContext dbContext,
        AwayModeService awayModeService,
        CancellationToken token)
    {
        return new McpHomeDashboard(
            await awayModeService.IsEnabledAsync(token),
            await GetDevicesAsync(dbContext, token),
            await GetWorkflowsAsync(dbContext, token));
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [Description("Lists all devices and their actions")]
    public static Task<List<McpDevice>> ListDevices(HomeManagementDbContext dbContext, CancellationToken token)
        => GetDevicesAsync(dbContext, token);

    [McpServerTool(UseStructuredContent = true)]
    [Description("Runs an action of a device, both found by name")]
    public static async Task<McpOperationResult> RunAction(
        [Description("Device name")] string deviceName,
        [Description("Action name")] string action,
        IDeviceActionExecutor deviceActionExecutor,
        HomeManagementDbContext dbContext,
        CancellationToken token)
    {
        var device = await dbContext.Devices.AsNoTracking()
            .Include(x => x.Actions)
            .Include(x => x.Configurations)
            .FirstOrDefaultAsync(x => x.Name == deviceName, token);
        if (device is null)
        {
            return new McpOperationResult(false, $"Device '{deviceName}' not found.");
        }

        var deviceAction = device.Actions.FirstOrDefault(x => x.Action == action);
        if (deviceAction is null)
        {
            return new McpOperationResult(false, $"Action '{action}' not found on device '{deviceName}'.");
        }

        var result = await deviceActionExecutor.ExecuteAsync(device, deviceAction, token);
        return new McpOperationResult(result.IsSuccess, result.Message);
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true)]
    [Description("Lists all workflows with their triggers")]
    public static Task<List<McpWorkflow>> ListWorkflows(HomeManagementDbContext dbContext, CancellationToken token)
        => GetWorkflowsAsync(dbContext, token);

    [McpServerTool(UseStructuredContent = true)]
    [Description("Runs a workflow by name now, regardless of its trigger conditions")]
    public static async Task<McpOperationResult> RunWorkflow(
        [Description("Workflow name")] string name,
        IWorkflowRunner workflowRunner,
        HomeManagementDbContext dbContext,
        CancellationToken token)
    {
        var workflow = await dbContext.Workflows.AsTracking().FirstOrDefaultAsync(x => x.Name == name, token);
        if (workflow is null)
        {
            return new McpOperationResult(false, $"Workflow '{name}' not found.");
        }

        var result = await workflowRunner.RunAsync(workflow, token: token);
        workflow.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(token);

        return result.IsSuccessful
            ? new McpOperationResult(true, $"Workflow '{name}' executed, {result.ExecutedActions} step(s) run.")
            : new McpOperationResult(false, $"Workflow '{name}' executed with errors: {string.Join(" | ", result.Errors)}");
    }

    [McpServerTool(UseStructuredContent = true, Idempotent = true)]
    [Description("Enables or disables a workflow by name")]
    public static async Task<McpOperationResult> SetWorkflowEnabled(
        [Description("Workflow name")] string name,
        [Description("true to enable, false to disable")] bool enabled,
        HomeManagementDbContext dbContext,
        CancellationToken token)
    {
        var workflow = await dbContext.Workflows.AsTracking().FirstOrDefaultAsync(x => x.Name == name, token);
        if (workflow is null)
        {
            return new McpOperationResult(false, $"Workflow '{name}' not found.");
        }

        workflow.IsEnabled = enabled;
        await dbContext.SaveChangesAsync(token);
        return new McpOperationResult(true, $"Workflow '{name}' {(enabled ? "enabled" : "disabled")}.");
    }

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

    private static async Task<List<McpDevice>> GetDevicesAsync(HomeManagementDbContext dbContext, CancellationToken token)
    {
        var devices = await dbContext.Devices.AsNoTracking()
            .Include(x => x.Actions)
            .OrderBy(x => x.Name)
            .ToListAsync(token);

        return devices
            .Select(d => new McpDevice(
                d.Name,
                d.Address,
                d.Description,
                d.Actions.Select(a => new McpDeviceAction(a.Action, a.CommandType.ToString())).ToList()))
            .ToList();
    }

    private static async Task<List<McpWorkflow>> GetWorkflowsAsync(HomeManagementDbContext dbContext, CancellationToken token)
    {
        var workflows = await dbContext.Workflows.AsNoTracking()
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerSourceAction)
            .ThenInclude(x => x!.Device)
            .Include(x => x.Steps)
            .OrderBy(x => x.Name)
            .ToListAsync(token);

        return workflows
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
}
