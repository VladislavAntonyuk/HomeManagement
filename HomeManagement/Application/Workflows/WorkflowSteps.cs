using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

/// <summary>
/// The step ran. <paramref name="Values"/> become the {LastStep.*} placeholders of the next steps.
/// </summary>
public sealed record class StepSucceeded(IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// The step failed. The workflow continues with the next step.
/// </summary>
public sealed record class StepFailed(string Error, IReadOnlyDictionary<string, string?> Values);

public union StepOutcome(StepSucceeded, StepFailed);

public static class WorkflowQueries
{
    /// <summary>
    /// Loads the triggers in their order, with the devices and actions they read.
    /// </summary>
    public static IQueryable<Workflow> WithTriggers(this IQueryable<Workflow> workflows) => workflows
        .Include(x => x.Triggers.OrderBy(t => t.Order))
        .ThenInclude(x => ((DeviceResultTrigger)x).DeviceAction)
        .ThenInclude(x => x.Device)
        .Include(x => x.Triggers)
        .ThenInclude(x => ((DeviceStatusTrigger)x).Device);

    /// <summary>
    /// Loads the steps in their order, with the device actions they run.
    /// </summary>
    public static IQueryable<Workflow> WithSteps(this IQueryable<Workflow> workflows) => workflows
        .Include(x => x.Steps.OrderBy(s => s.Order))
        .ThenInclude(x => ((DeviceActionStep)x).DeviceAction)
        .ThenInclude(x => x.Device);
}
