using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.DeviceManagement;

public interface IDeviceActionExecutor
{
    Task<DeviceActionExecutionResult> ExecuteAsync(Device device, DeviceAction action, CancellationToken token = default);
}

public record DeviceActionExecutionResult(bool IsSuccess, string Message, string? RawResponse = null);

public class DeviceActionExecutor(IDeviceHub deviceHub, DeviceStateStore stateStore) : IDeviceActionExecutor
{
    public async Task<DeviceActionExecutionResult> ExecuteAsync(Device device, DeviceAction action, CancellationToken token = default)
    {
        if (stateStore.IsOnline(device.DeviceId) != true)
        {
            return new DeviceActionExecutionResult(false, $"'{device.Name}' is offline.");
        }

        if (action.CommandType == Shared.CommandType.Get)
        {
            return stateStore.GetTelemetry(device.DeviceId, action.Command) is { } telemetry
                ? new DeviceActionExecutionResult(true, telemetry.Payload, telemetry.Payload)
                : new DeviceActionExecutionResult(false, $"'{device.Name}' has not reported '{action.Action}' yet.");
        }

        var response = await deviceHub.SendCommandAsync(device.DeviceId, action.Command, token);
        var message = string.IsNullOrEmpty(response.Output)
            ? response.IsSuccess ? $"{action.Action} successfully executed" : "Error has occurred"
            : response.Output;

        return new DeviceActionExecutionResult(response.IsSuccess, message, response.Output);
    }
}
