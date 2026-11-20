using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

public record WorkflowTriggerPropertyOption(string Path, WorkflowTriggerValueType ValueType, string? SampleValue);

/// <param name="Error">Why the action could not be read, or null when it was.</param>
public record WorkflowTriggerPreview(string? Error, string? RawResponse, IReadOnlyList<WorkflowTriggerPropertyOption> Properties)
{
    public bool IsSuccess => Error is null;
}

/// <summary>
/// Reads the current value of a device action, so the workflow editor can offer its JSON properties for a trigger.
/// </summary>
public class WorkflowTriggerPreviewService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    DeviceActionExecutor deviceActionExecutor)
{
    public async Task<WorkflowTriggerPreview> PreviewAsync(int actionId, CancellationToken token = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        if (await dbContext.Set<DeviceAction>().Include(x => x.Device).FirstOrDefaultAsync(x => x.Id == actionId, token) is not { } action)
        {
            return new WorkflowTriggerPreview("The action no longer exists.", null, []);
        }

        var execution = await deviceActionExecutor.ExecuteAsync(action.Device, action, token);
        if (!execution.IsSuccess)
        {
            return new WorkflowTriggerPreview(execution.Message, execution.RawResponse, []);
        }

        var response = execution.RawResponse ?? execution.Message;
        var properties = JsonValueReader.GetProperties(response);
        return properties.Count == 0
            ? new WorkflowTriggerPreview("No comparable values found in the response.", response, [])
            : new WorkflowTriggerPreview(null, response, properties);
    }
}
