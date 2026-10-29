using System.Collections.Concurrent;

namespace HomeManagement.Application.DeviceManagement;

public sealed record DeviceTelemetry(string Payload, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Latest status and telemetry reported by devices. Rebuilt from retained MQTT messages after every hub start.
/// </summary>
public sealed class DeviceStateStore
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised when a device goes online or offline.
    /// </summary>
    public event Action? StatusChanged;

    /// <summary>
    /// Raised when a device is registered or its actions change.
    /// </summary>
    public event Action? DevicesChanged;

    public bool? IsOnline(string deviceId) => _devices.TryGetValue(deviceId, out var state) ? state.IsOnline : null;

    public DeviceTelemetry? GetTelemetry(string deviceId, string key)
        => _devices.TryGetValue(deviceId, out var state) && state.Telemetry.TryGetValue(key, out var telemetry) ? telemetry : null;

    public IReadOnlyCollection<string> GetTelemetryKeys(string deviceId)
        => _devices.TryGetValue(deviceId, out var state) ? state.Telemetry.Keys.ToList() : [];

    public void SetStatus(string deviceId, bool? isOnline)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        if (state.IsOnline == isOnline)
        {
            return;
        }

        state.IsOnline = isOnline;
        StatusChanged?.Invoke();
    }

    public void SetTelemetry(string deviceId, string key, string? payload)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        if (string.IsNullOrEmpty(payload))
        {
            state.Telemetry.TryRemove(key, out _);
            return;
        }

        state.Telemetry[key] = new DeviceTelemetry(payload, DateTimeOffset.UtcNow);
    }

    public void Remove(string deviceId)
    {
        if (_devices.TryRemove(deviceId, out _))
        {
            StatusChanged?.Invoke();
        }
    }

    public void NotifyDevicesChanged() => DevicesChanged?.Invoke();

    private sealed class DeviceState
    {
        public bool? IsOnline { get; set; }

        public ConcurrentDictionary<string, DeviceTelemetry> Telemetry { get; } = new(StringComparer.Ordinal);
    }
}
