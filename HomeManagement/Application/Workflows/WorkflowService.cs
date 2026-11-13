using System.Linq.Expressions;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

public sealed record WorkflowSummary(int Total, int Enabled, DateTimeOffset? LastRunAtUtc);

public sealed class WorkflowService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IWorkflowRunner workflowRunner)
{
    public async Task<List<Workflow>> GetWorkflowsAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        return await db.Workflows
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerSourceAction)
            .ThenInclude(x => x!.Device)
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerDevice)
            .Include(x => x.Steps.OrderBy(s => s.Order))
            .ThenInclude(x => x.DeviceAction)
            .ThenInclude(x => x!.Device)
            .OrderBy(x => x.Name)
            .ToListAsync(token);
    }

    public async Task<WorkflowSummary> GetSummaryAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflows = await db.Workflows
            .Select(x => new { x.IsEnabled, x.LastTriggeredAtUtc })
            .ToListAsync(token);

        return new WorkflowSummary(workflows.Count, workflows.Count(x => x.IsEnabled), workflows.Max(x => x.LastTriggeredAtUtc));
    }

    public Task<OperationResult> RunAsync(int id, CancellationToken token = default)
        => RunAsync(x => x.Id == id, "Workflow not found.", token);

    public Task<OperationResult> RunAsync(string name, CancellationToken token = default)
        => RunAsync(x => x.Name == name, $"Workflow '{name}' not found.", token);

    public Task<OperationResult> SetEnabledAsync(int id, bool enabled, CancellationToken token = default)
        => SetEnabledAsync(x => x.Id == id, enabled, "Workflow not found.", token);

    public Task<OperationResult> SetEnabledAsync(string name, bool enabled, CancellationToken token = default)
        => SetEnabledAsync(x => x.Name == name, enabled, $"Workflow '{name}' not found.", token);

    public async Task<OperationResult> AddAsync(Workflow workflow, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"Workflow '{workflow.Name}' added.");
    }

    public async Task<OperationResult> UpdateAsync(int id, Workflow updated, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows
            .AsTracking()
            .Include(x => x.TriggerConditions)
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == id, token);
        if (workflow is null)
        {
            return OperationResult.NotFound("Workflow not found.");
        }

        workflow.Name = updated.Name;
        workflow.Description = updated.Description;
        workflow.IsEnabled = updated.IsEnabled;
        workflow.ConditionOperator = updated.ConditionOperator;
        workflow.RepeatWhileMatched = updated.RepeatWhileMatched;
        workflow.TriggerConditions = updated.TriggerConditions;
        workflow.Steps = updated.Steps;
        workflow.LastConditionMatched = null;
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"Workflow '{workflow.Name}' updated.");
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows.AsTracking().FirstOrDefaultAsync(x => x.Id == id, token);
        if (workflow is null)
        {
            return OperationResult.NotFound("Workflow not found.");
        }

        db.Workflows.Remove(workflow);
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"Deleted workflow '{workflow.Name}'.");
    }

    private async Task<OperationResult> RunAsync(Expression<Func<Workflow, bool>> predicate, string notFound, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows.AsTracking().FirstOrDefaultAsync(predicate, token);
        if (workflow is null)
        {
            return OperationResult.NotFound(notFound);
        }

        var result = await workflowRunner.RunAsync(workflow, token: token);
        workflow.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return result.IsSuccessful
            ? OperationResult.Success($"Workflow '{workflow.Name}' executed, {result.ExecutedActions} step(s) run.")
            : OperationResult.Failure($"Workflow '{workflow.Name}' executed with errors: {string.Join(" | ", result.Errors)}");
    }

    private async Task<OperationResult> SetEnabledAsync(Expression<Func<Workflow, bool>> predicate, bool enabled, string notFound, CancellationToken token)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows.AsTracking().FirstOrDefaultAsync(predicate, token);
        if (workflow is null)
        {
            return OperationResult.NotFound(notFound);
        }

        workflow.IsEnabled = enabled;
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"Workflow '{workflow.Name}' {(enabled ? "enabled" : "disabled")}.");
    }
}
