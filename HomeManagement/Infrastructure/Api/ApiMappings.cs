using HomeManagement.Application;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;

namespace HomeManagement.Infrastructure.Api;

/// <summary>
/// One mapping from the hub's model to the shared contracts, used by the REST API and the MCP tools.
/// </summary>
public static class ApiMappings
{
    extension(DeviceOverview overview)
    {
        public DeviceDto ToDto() => new(
            overview.Device.Id,
            overview.Device.Name,
            overview.Device.DeviceId,
            overview.Device.Description,
            overview.Presence.Status,
            overview.Presence.LastSeenUtc,
            overview.Address,
            overview.Metrics,
            [.. overview.Device.Actions.Select(x => new DeviceActionDto(x.Id, x.Action, x.CommandType))]);

        /// <summary>
        /// The address the device reported, or null when it did not report one.
        /// </summary>
        public string? Address => string.IsNullOrEmpty(overview.Info?.Address) ? null : overview.Info.Address;
    }

    extension(Workflow workflow)
    {
        public WorkflowDto ToDto() => new(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.IsEnabled,
            workflow.RepeatWhileMatched,
            WorkflowFormatter.FormatTrigger(workflow),
            workflow.Steps.Count,
            workflow.LastTriggeredAtUtc);
    }

    extension(OperationResult result)
    {
        public OperationResultDto ToDto() => new(result.Message);
    }
}
