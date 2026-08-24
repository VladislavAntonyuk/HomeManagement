namespace HomeManagement.Infrastructure.Database.Entities;

public class Workflow
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    public WorkflowConditionOperator ConditionOperator { get; set; } = WorkflowConditionOperator.And;

    public DateTimeOffset? LastTriggeredAtUtc { get; set; }

    public bool? LastConditionMatched { get; set; }

    public IList<WorkflowTriggerCondition> TriggerConditions { get; init; } = [];

    public IList<WorkflowStep> Steps { get; init; } = [];
}