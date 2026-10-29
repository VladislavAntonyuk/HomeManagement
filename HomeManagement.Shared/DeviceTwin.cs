namespace HomeManagement.Shared;

/// <summary>
/// Describes a device and the actions it supports. Published retained to <see cref="DeviceTopics.Twin"/>.
/// </summary>
public class DeviceTwin
{
    public required string Name { get; init; }

    public IList<DeviceAction> Actions { get; init; } = [];
}
