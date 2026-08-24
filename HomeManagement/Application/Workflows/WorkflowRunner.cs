using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.WebHooks;
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
    IServiceProvider serviceProvider,
    SenderRequestFactory senderRequestFactory,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    public async Task<WorkflowExecutionResult> RunAsync(Workflow workflow, WorkflowExecutionContext? context = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var executionContext = context ?? WorkflowExecutionContext.Empty;
        var steps = workflow.Steps;

        if (steps.Count == 0)
        {
            return new WorkflowExecutionResult(0, []);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var deviceNames = steps
            .Where(x => x.StepType is WorkflowStepType.DeviceAction && !string.IsNullOrWhiteSpace(x.DeviceName))
            .Select(x => x.DeviceName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var devices = await dbContext.Devices
            .AsNoTracking()
            .Include(x => x.Actions)
            .Include(x => x.Configurations)
            .Where(x => deviceNames.Contains(x.Name))
            .ToDictionaryAsync(x => x.Name, StringComparer.Ordinal, token);

        var executedActions = 0;
        var errors = new List<string>();
        var dynamicValues = new Dictionary<string, string?>(executionContext.Values, StringComparer.Ordinal)
        {
            ["Workflow.Name"] = workflow.Name
        };

        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();

            switch (step.StepType)
            {
                case WorkflowStepType.DeviceAction:
                    {
                        if (string.IsNullOrWhiteSpace(step.DeviceName) || string.IsNullOrWhiteSpace(step.ActionName))
                        {
                            errors.Add("DeviceAction step requires DeviceName and ActionName.");
                            continue;
                        }

                        if (!devices.TryGetValue(step.DeviceName, out var device))
                        {
                            errors.Add($"Device '{step.DeviceName}' not found.");
                            continue;
                        }

                        var deviceAction = device.Actions.FirstOrDefault(x => string.Equals(x.Action, step.ActionName, StringComparison.Ordinal));
                        if (deviceAction is null)
                        {
                            errors.Add($"Action '{step.ActionName}' not found on device '{step.DeviceName}'.");
                            continue;
                        }

                        var result = await deviceActionExecutor.ExecuteAsync(device, deviceAction, token);
                        dynamicValues["LastStep.Type"] = nameof(WorkflowStepType.DeviceAction);
                        dynamicValues["LastStep.Success"] = result.IsSuccess.ToString();
                        dynamicValues["LastStep.Message"] = result.Message;
                        dynamicValues["LastStep.Device"] = step.DeviceName;
                        dynamicValues["LastStep.Action"] = step.ActionName;

                        if (!result.IsSuccess)
                        {
                            errors.Add($"{step.DeviceName}/{step.ActionName}: {result.Message}");
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

                        await using var scope = serviceProvider.CreateAsyncScope();
                        var senders = scope.ServiceProvider.GetServices<ISender>();
                        var senderErrors = new List<string>();

                        foreach (var sender in senders)
                        {
                            var request = senderRequestFactory.Create(payload, sender);
                            if (request is null)
                            {
                                continue;
                            }

                            var sendResult = await sender.Send(request, token);
                            if (!sendResult.IsSuccessful)
                            {
                                senderErrors.Add($"{sender.GetType().Name}: {sendResult.Error}");
                            }
                        }

                        dynamicValues["LastStep.Type"] = nameof(WorkflowStepType.Notify);
                        dynamicValues["LastStep.Message"] = payload;
                        dynamicValues["LastStep.Success"] = (senderErrors.Count == 0).ToString();

                        if (senderErrors.Count > 0)
                        {
                            errors.Add($"Notify failed: {string.Join(" | ", senderErrors)}");
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