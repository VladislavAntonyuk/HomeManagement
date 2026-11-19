using HomeManagement.Shared;

namespace HomeManagement.DeviceSdk;

/// <summary>
/// What a device can do. <see cref="DeviceAgent"/> handles the protocol: twin, status, heartbeat, telemetry and command responses.
/// </summary>
public interface IDeviceHandler
{
    /// <summary>
    /// Raised when <see cref="GetActions"/> changes, so the twin is published again.
    /// </summary>
    event Action? ActionsChanged;

    /// <summary>
    /// Actions of the device, without the built-in info action.
    /// </summary>
    IReadOnlyList<DeviceAction> GetActions();

    /// <summary>
    /// Runs a <see cref="CommandType.Post"/> action.
    /// </summary>
    Task<CommandResponse> ExecuteAsync(string command, CancellationToken token);

    /// <summary>
    /// Reads the value of a <see cref="CommandType.Get"/> action. Called every telemetry interval; null publishes nothing.
    /// </summary>
    Task<string?> ReadAsync(string key, CancellationToken token);

    /// <summary>
    /// Completes when <see cref="GetActions"/> is known, e.g. after the device read its configuration.
    /// The agent waits for it, for a limited time, before it announces the twin, so the hub does not drop actions that are only late.
    /// </summary>
    Task WaitUntilReadyAsync(CancellationToken token) => Task.CompletedTask;

    /// <summary>
    /// Metrics published in the device info.
    /// </summary>
    IReadOnlyDictionary<string, string> GetMetrics() => DeviceMetrics.GetDefault();
}
