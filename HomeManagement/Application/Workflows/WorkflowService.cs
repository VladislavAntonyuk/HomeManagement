using System.Linq.Expressions;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

public sealed record WorkflowSummary(int Total, int Enabled, DateTimeOffset? LastRunAtUtc);

public sealed class WorkflowService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    WorkflowRunner workflowRunner,
    TimeProvider timeProvider)
{
    public async Task<List<Workflow>> GetWorkflowsAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        return await db.Workflows
            .WithTriggers()
            .WithSteps()
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
        if (await db.Workflows.AnyAsync(x => x.Name == workflow.Name, token))
        {
            return OperationResult.Failure($"Another workflow is already called '{workflow.Name}'.");
        }

        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(token);
        return OperationResult.Success($"Workflow '{workflow.Name}' added.");
    }

    public async Task<OperationResult> UpdateAsync(int id, Workflow updated, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var workflow = await db.Workflows
            .AsTracking()
            .Include(x => x.Triggers)
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == id, token);
        if (workflow is null)
        {
            return OperationResult.NotFound("Workflow not found.");
        }

        if (await db.Workflows.AnyAsync(x => x.Id != id && x.Name == updated.Name, token))
        {
            return OperationResult.Failure($"Another workflow is already called '{updated.Name}'.");
        }

        workflow.Name = updated.Name;
        workflow.Description = updated.Description;
        workflow.IsEnabled = updated.IsEnabled;
        workflow.ConditionOperator = updated.ConditionOperator;
        workflow.RepeatWhileMatched = updated.RepeatWhileMatched;
        // New rows replace the old ones, so the evaluation state starts over, e.g. a device status trigger does not run on save.
        workflow.Triggers = updated.Triggers;
        workflow.Steps = updated.Steps;
        workflow.WasMatched = null;
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
        workflow.LastTriggeredAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(token);

        return result.IsSuccessful
            ? OperationResult.Success($"Workflow '{workflow.Name}' executed, {result.ExecutedSteps} step(s) run.")
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
