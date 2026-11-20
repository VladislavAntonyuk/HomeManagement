namespace HomeManagement.Infrastructure.Database.Entities;

public class Workflow
{
    public int Id { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// How the triggers combine. A workflow without triggers only runs when someone runs it.
    /// </summary>
    public WorkflowConditionOperator ConditionOperator { get; set; } = WorkflowConditionOperator.And;

    /// <summary>
    /// Runs on every regular check while the triggers match, instead of once when they start to match.
    /// </summary>
    public bool RepeatWhileMatched { get; set; }

    public DateTimeOffset? LastTriggeredAtUtc { get; set; }

    /// <summary>
    /// Whether the triggers matched at the previous check, or null when they could not be evaluated.
    /// </summary>
    public bool? WasMatched { get; set; }

    public IList<WorkflowTrigger> Triggers { get; set; } = [];

    public IList<WorkflowStep> Steps { get; set; } = [];
}
