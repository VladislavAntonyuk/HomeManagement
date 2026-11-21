using System.Text.RegularExpressions;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Notifications;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

public record WorkflowExecutionResult(int ExecutedSteps, IReadOnlyList<string> Errors)
{
    public bool IsSuccessful => Errors.Count == 0;
}

public partial class WorkflowRunner(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    DeviceActionExecutor deviceActionExecutor,
    INotificationSender notificationSender,
    ILogger<WorkflowRunner> logger)
{
    private const string LastStepPrefix = "LastStep.";

    /// <param name="placeholders">The {Trigger.*} placeholders of the trigger that started the workflow.</param>
    public async Task<WorkflowExecutionResult> RunAsync(Workflow workflow, IReadOnlyDictionary<string, string?>? placeholders = null, CancellationToken token = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var steps = await dbContext.Set<WorkflowStep>()
            .Where(x => x.WorkflowId == workflow.Id)
            .Include(x => ((DeviceActionStep)x).DeviceAction)
            .ThenInclude(x => x.Device)
            .OrderBy(x => x.Order)
            .ToListAsync(token);

        var executedSteps = 0;
        List<string> errors = [];
        Dictionary<string, string?> values = new(placeholders ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase)
        {
            ["Workflow.Name"] = workflow.Name
        };

        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();

            StepOutcome outcome = step switch
            {
                DeviceActionStep deviceAction => await RunDeviceActionAsync(deviceAction, token),
                DelayStep delay => await DelayAsync(delay, token),
                NotifyStep notify => await NotifyAsync(notify, values, token)
            };

            switch (outcome)
            {
                case StepSucceeded succeeded:
                    executedSteps++;
                    SetLastStep(values, step, true, succeeded.Values);
                    break;
                case StepFailed failed:
                    errors.Add(failed.Error);
                    SetLastStep(values, step, false, failed.Values);
                    break;
            }
        }

        logger.LogInformation(
            "Workflow {WorkflowName} executed. Successful steps: {ExecutedSteps}. Errors: {ErrorCount}.",
            workflow.Name,
            executedSteps,
            errors.Count);

        return new WorkflowExecutionResult(executedSteps, errors);
    }

    /// <summary>
    /// Replaces the {LastStep.*} placeholders, so values of an earlier step never leak into a later one.
    /// </summary>
    private static void SetLastStep(Dictionary<string, string?> placeholders, WorkflowStep step, bool isSuccess, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var key in placeholders.Keys.Where(x => x.StartsWith(LastStepPrefix, StringComparison.Ordinal)).ToList())
        {
            placeholders.Remove(key);
        }

        placeholders[LastStepPrefix + "Type"] = step switch
        {
            DeviceActionStep => "DeviceAction",
            DelayStep => "Delay",
            NotifyStep => "Notify"
        };
        placeholders[LastStepPrefix + "Success"] = isSuccess.ToString();
        foreach (var (key, value) in values)
        {
            placeholders[LastStepPrefix + key] = value;
        }
    }

    private async Task<StepOutcome> RunDeviceActionAsync(DeviceActionStep step, CancellationToken token)
    {
        var action = step.DeviceAction;
        var device = action.Device;
        var result = await deviceActionExecutor.ExecuteAsync(device, action, token);
        Dictionary<string, string?> values = new()
        {
            ["Message"] = result.Message,
            ["Device"] = device.Name,
            ["Action"] = action.Action
        };

        return result.IsSuccess
            ? new StepSucceeded(values)
            : new StepFailed($"{device.Name}/{action.Action}: {result.Message}", values);
    }

    private static async Task<StepOutcome> DelayAsync(DelayStep step, CancellationToken token)
    {
        await Task.Delay(step.Delay, token);
        return new StepSucceeded(new Dictionary<string, string?> { ["DelaySeconds"] = ((int)step.Delay.TotalSeconds).ToString() });
    }

    private async Task<StepOutcome> NotifyAsync(NotifyStep step, IReadOnlyDictionary<string, string?> placeholders, CancellationToken token)
    {
        var payload = $"{step.Title}{Environment.NewLine}{RenderTemplate(step.MessageTemplate, placeholders)}";
        var result = await notificationSender.SendAsync(payload, token);
        Dictionary<string, string?> values = new() { ["Message"] = payload };

        return result.IsSuccess
            ? new StepSucceeded(values)
            : new StepFailed($"Notify failed: {result.Message}", values);
    }

    /// <summary>
    /// Replaces every {Key} in one pass, so a value that itself contains {Other.Key}, e.g. device JSON, is not replaced again.
    /// Unknown placeholders stay as they are.
    /// </summary>
    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string?> placeholders)
        => Placeholder().Replace(template, match => placeholders.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : match.Value);

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();
}
