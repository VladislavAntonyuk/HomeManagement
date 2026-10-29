using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Notifications;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Workflows;

public interface IWorkflowRunner
{
    Task<WorkflowExecutionResult> RunAsync(Workflow workflow, WorkflowExecutionContext? context = null, CancellationToken token = default);
}

public record WorkflowExecutionContext(IReadOnlyDictionary<string, string?> Values)
{
    public static readonly WorkflowExecutionContext Empty = new(new Dictionary<string, string?>());
}

public record WorkflowExecutionResult(int ExecutedActions, IReadOnlyList<string> Errors)
{
    public bool IsSuccessful => Errors.Count == 0;
}

public class WorkflowRunner(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDeviceActionExecutor deviceActionExecutor,
    INotificationSender notificationSender,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    public async Task<WorkflowExecutionResult> RunAsync(Workflow workflow, WorkflowExecutionContext? context = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var executionContext = context ?? WorkflowExecutionContext.Empty;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var steps = await dbContext.Set<WorkflowStep>()
            .AsNoTracking()
            .Where(x => x.WorkflowId == workflow.Id)
            .Include(x => x.DeviceAction)
            .ThenInclude(x => x!.Device)
            .OrderBy(x => x.Order)
            .ToListAsync(token);

        if (steps.Count == 0)
        {
            return new WorkflowExecutionResult(0, []);
        }

        var executedActions = 0;
        var errors = new List<string>();
        var dynamicValues = new Dictionary<string, string?>(executionContext.Values, StringComparer.Ordinal)
        {
            ["Workflow.Name"] = workflow.Name
        };

        foreach (var (stepIndex, step) in steps.Index())
        {
            token.ThrowIfCancellationRequested();

            switch (step.StepType)
            {
                case WorkflowStepType.DeviceAction:
                    {
                        if (step.DeviceAction is not { } deviceAction)
                        {
                            errors.Add($"Step {stepIndex + 1}: its device action no longer exists, select one in the workflow.");
                            continue;
                        }

                        var device = deviceAction.Device;
                        var result = await deviceActionExecutor.ExecuteAsync(device, deviceAction, token);
                        dynamicValues["LastStep.Type"] = nameof(WorkflowStepType.DeviceAction);
                        dynamicValues["LastStep.Success"] = result.IsSuccess.ToString();
                        dynamicValues["LastStep.Message"] = result.Message;
                        dynamicValues["LastStep.Device"] = device.Name;
                        dynamicValues["LastStep.Action"] = deviceAction.Action;

                        if (!result.IsSuccess)
                        {
                            errors.Add($"{device.Name}/{deviceAction.Action}: {result.Message}");
                            continue;
                        }

                        executedActions++;
                        break;
                    }

                case WorkflowStepType.Delay:
                    {
                        var delaySeconds = step.DelaySeconds.GetValueOrDefault();
                        if (delaySeconds <= 0)
                        {
                            errors.Add("Delay step requires DelaySeconds > 0.");
                            continue;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                        dynamicValues["LastStep.Type"] = nameof(WorkflowStepType.Delay);
                        dynamicValues["LastStep.Success"] = true.ToString();
                        dynamicValues["LastStep.DelaySeconds"] = delaySeconds.ToString();
                        executedActions++;
                        break;
                    }

                case WorkflowStepType.Notify:
                    {
                        var title = step.NotifyTitle ?? "Workflow Notification";
                        var template = step.NotifyMessageTemplate;
                        if (string.IsNullOrWhiteSpace(template))
                        {
                            errors.Add("Notify step requires NotifyMessageTemplate.");
                            continue;
                        }

                        var message = RenderTemplate(template, dynamicValues);
                        var payload = $"{title}{Environment.NewLine}{message}";

                        var sendResult = await notificationSender.SendAsync(payload, token);

                        dynamicValues["LastStep.Type"] = nameof(WorkflowStepType.Notify);
                        dynamicValues["LastStep.Message"] = payload;
                        dynamicValues["LastStep.Success"] = sendResult.IsSuccessful.ToString();

                        if (!sendResult.IsSuccessful)
                        {
                            errors.Add($"Notify failed: {sendResult.Error}");
                            continue;
                        }

                        executedActions++;
                        break;
                    }
            }
        }

        logger.LogInformation(
            "Workflow {WorkflowName} executed. Successful steps: {ExecutedActions}. Errors: {ErrorCount}.",
            workflow.Name,
            executedActions,
            errors.Count);

        return new WorkflowExecutionResult(executedActions, errors);
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string?> values)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace($"{{{key}}}", value ?? string.Empty, StringComparison.Ordinal);
        }

        return result;
    }
}