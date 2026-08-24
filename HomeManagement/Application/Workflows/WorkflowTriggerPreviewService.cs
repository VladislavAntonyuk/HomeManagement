using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public interface IWorkflowTriggerPreviewService
{
    Task<WorkflowTriggerPreviewResult> PreviewAsync(string deviceName, string actionName, CancellationToken token = default);
}

public record WorkflowTriggerPropertyOption(string Path, WorkflowTriggerValueType ValueType, string? SampleValue);

public record WorkflowTriggerPreviewResult(
    bool IsSuccess,
    string? Error,
    string? RawResponse,
    IReadOnlyList<WorkflowTriggerPropertyOption> Properties);

public class WorkflowTriggerPreviewService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDeviceActionExecutor deviceActionExecutor) : IWorkflowTriggerPreviewService
{
    public async Task<WorkflowTriggerPreviewResult> PreviewAsync(string deviceName, string actionName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return new WorkflowTriggerPreviewResult(false, "Device is required.", null, []);
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            return new WorkflowTriggerPreviewResult(false, "Action is required.", null, []);
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var device = await dbContext.Devices
            .AsNoTracking()
            .Include(x => x.Actions)
            .Include(x => x.Configurations)
            .FirstOrDefaultAsync(x => x.Name == deviceName, token);

        if (device is null)
        {
            return new WorkflowTriggerPreviewResult(false, $"Device '{deviceName}' not found.", null, []);
        }

        var action = device.Actions.FirstOrDefault(x => string.Equals(x.Action, actionName, StringComparison.Ordinal));
        if (action is null)
        {
            return new WorkflowTriggerPreviewResult(false, $"Action '{actionName}' not found on device '{deviceName}'.", null, []);
        }

        var execution = await deviceActionExecutor.ExecuteAsync(device, action, token);
        if (!execution.IsSuccess)
        {
            return new WorkflowTriggerPreviewResult(false, execution.Message, execution.RawResponse, []);
        }

        var response = execution.RawResponse ?? execution.Message;
        var properties = ParseProperties(response);

        return new WorkflowTriggerPreviewResult(true, null, response, properties);
    }

    private static IReadOnlyList<WorkflowTriggerPropertyOption> ParseProperties(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var result = new List<WorkflowTriggerPropertyOption>();
            AddProperties(document.RootElement, "$", result);
            return result;
        }
        catch (JsonException)
        {
            return [new WorkflowTriggerPropertyOption("$", WorkflowTriggerValueType.Text, response)];
        }
    }

    private static void AddProperties(JsonElement element, string path, ICollection<WorkflowTriggerPropertyOption> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddProperties(property.Value, $"{path}.{property.Name}", result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AddProperties(item, $"{path}[{index}]", result);
                    index++;
                }
                break;

            default:
                result.Add(new WorkflowTriggerPropertyOption(path, MapValueType(element), ToSampleValue(element)));
                break;
        }
    }

    private static WorkflowTriggerValueType MapValueType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => WorkflowTriggerValueType.Number,
            JsonValueKind.True or JsonValueKind.False => WorkflowTriggerValueType.Boolean,
            JsonValueKind.String => WorkflowTriggerValueType.Text,
            JsonValueKind.Null or JsonValueKind.Undefined => WorkflowTriggerValueType.Null,
            _ => WorkflowTriggerValueType.Json
        };
    }

    private static string? ToSampleValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}