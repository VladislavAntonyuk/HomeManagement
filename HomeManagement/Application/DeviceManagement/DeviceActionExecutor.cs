using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// The action ran: a Get or Event action returned the latest telemetry, or the device answered a Post action successfully.
/// </summary>
/// <param name="Output">What the device returned.</param>
public sealed record class ActionSucceeded(string Action, string? Output);

/// <summary>
/// The device is not online, so the action was not sent.
/// </summary>
public sealed record class ActionDeviceOffline(string DeviceName);

/// <summary>
/// A Get or Event action has no telemetry yet.
/// </summary>
public sealed record class ActionValueMissing(string DeviceName, string Action, bool IsEvent);

/// <summary>
/// The device answered a Post action with an error, or did not answer.
/// </summary>
public sealed record class ActionFailed(string? Output);

/// <summary>
/// Every outcome of running a device action. Callers that need more than success and a message switch over the cases.
/// </summary>
public union DeviceActionResult(ActionSucceeded, ActionDeviceOffline, ActionValueMissing, ActionFailed);

public static class DeviceActionResultExtensions
{
    extension(DeviceActionResult result)
    {
        public bool IsSuccess => result is ActionSucceeded;

        public string Message => result switch
        {
            ActionSucceeded { Output: { Length: > 0 } output } => output,
            ActionSucceeded succeeded => string.Format(Strings.Action_Succeeded, succeeded.Action),
            ActionDeviceOffline offline => string.Format(Strings.Action_DeviceOffline, offline.DeviceName),
            // Events are not retained, so the hub only knows the ones that happened since it started.
            ActionValueMissing { IsEvent: true } missing => string.Format(Strings.Action_NoEventSinceStart, missing.Action, missing.DeviceName),
            ActionValueMissing missing => string.Format(Strings.Action_NotReportedYet, missing.DeviceName, missing.Action),
            ActionFailed { Output: { Length: > 0 } output } => output,
            ActionFailed => Strings.Common_ErrorOccurred
        };

        /// <summary>
        /// What the device returned, or null when the action did not reach it.
        /// </summary>
        public string? RawResponse => result switch
        {
            ActionSucceeded succeeded => succeeded.Output,
            ActionFailed failed => failed.Output,
            ActionDeviceOffline or ActionValueMissing => null
        };
    }
}

public sealed class DeviceActionExecutor(DeviceHub deviceHub, DeviceStateStore stateStore)
{
    public async Task<DeviceActionResult> ExecuteAsync(Device device, DeviceAction action, CancellationToken token = default)
    {
        if (!stateStore.IsOnline(device.DeviceId))
        {
            return new ActionDeviceOffline(device.Name);
        }

        if (action.CommandType is Shared.CommandType.Get or Shared.CommandType.Event)
        {
            return stateStore.GetTelemetry(device.DeviceId, action.Command) is { } telemetry
                ? new ActionSucceeded(action.Action, telemetry.Payload)
                : new ActionValueMissing(device.Name, action.Action, action.CommandType == Shared.CommandType.Event);
        }

        var response = await deviceHub.SendCommandAsync(device.DeviceId, action.Command, token);
        stateStore.SetCommandOutput(device.DeviceId, action.Command, response);
        return response.IsSuccess ? new ActionSucceeded(action.Action, response.Output) : new ActionFailed(response.Output);
    }
}
