namespace HomeManagement.Shared;

/// <summary>
/// An action a device exposes.
/// <see cref="CommandType.Get"/> actions are read from telemetry published to <see cref="DeviceTopics.Telemetry"/>.
/// <see cref="CommandType.Post"/> actions are executed through <see cref="DeviceTopics.Command"/>.
/// </summary>
/// <param name="Action">Display name of the action.</param>
/// <param name="CommandType">How the action is used.</param>
/// <param name="Command">Key of the action, used as the last part of the telemetry or command topic.</param>
public record DeviceAction(string Action, CommandType CommandType, string Command);
