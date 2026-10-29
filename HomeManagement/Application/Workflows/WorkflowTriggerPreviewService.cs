using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public interface IWorkflowTriggerPreviewService
{
    Task<WorkflowTriggerPreviewResult> PreviewAsync(string deviceName, string actionName, CancellationToken token = default);
}

public record WorkflowTriggerPropertyOption(string Path, WorkflowTriggerValueType ValueType, string? SampleValue);

public record WorkflowTriggerPreviewResult(
    bool IsSuccess,
    string? Error,
    string? RawResponse,
    IReadOnlyList<WorkflowTriggerPropertyOption> Properties);

public class WorkflowTriggerPreviewService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDeviceActionExecutor deviceActionExecutor) : IWorkflowTriggerPreviewService
{
    public async Task<WorkflowTriggerPreviewResult> PreviewAsync(string deviceName, string actionName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return new WorkflowTriggerPreviewResult(false, "Device is required.", null, []);
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            return new WorkflowTriggerPreviewResult(false, "Action is required.", null, []);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var device = await dbContext.Devices
            .AsNoTracking()
            .Include(x => x.Actions)
            .FirstOrDefaultAsync(x => x.Name == deviceName, token);

        if (device is null)
        {
            return new WorkflowTriggerPreviewResult(false, $"Device '{deviceName}' not found.", null, []);
        }

        var action = device.Actions.FirstOrDefault(x => string.Equals(x.Action, actionName, StringComparison.Ordinal));
        if (action is null)
        {
            return new WorkflowTriggerPreviewResult(false, $"Action '{actionName}' not found on device '{deviceName}'.", null, []);
        }

        var execution = await deviceActionExecutor.ExecuteAsync(device, action, token);
        if (!execution.IsSuccess)
        {
            return new WorkflowTriggerPreviewResult(false, execution.Message, execution.RawResponse, []);
        }

        var response = execution.RawResponse ?? execution.Message;
        return new WorkflowTriggerPreviewResult(true, null, response, JsonValueReader.GetProperties(response));
    }
}
