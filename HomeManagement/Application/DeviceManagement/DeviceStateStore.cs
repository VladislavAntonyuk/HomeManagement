using System.Collections.Concurrent;
using System.Text.Json;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.DeviceManagement;

public sealed record DeviceTelemetry(string Payload, DateTimeOffset ReceivedAtUtc);

/// <param name="IsOnline">Whether the device is online, or null when it has not reported a status.</param>
/// <param name="LastSeenUtc">When the hub last received any message of the device.</param>
/// <param name="HeartbeatInterval">How often the device promised to publish its info.</param>
/// <param name="IsHeartbeatMissed">The device reports online, but stopped sending messages, e.g. because it lost power and the broker did not notice.</param>
public sealed record DevicePresence(bool? IsOnline, DateTimeOffset? LastSeenUtc, TimeSpan HeartbeatInterval, bool IsHeartbeatMissed);

/// <summary>
/// Latest status and telemetry reported by devices. Rebuilt from retained MQTT messages after every hub start.
/// </summary>
public sealed class DeviceStateStore(TimeProvider timeProvider, IOptions<DevicePresenceOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised when a device goes online or offline.
    /// </summary>
    public event Action? StatusChanged;

    /// <summary>
    /// Raised when a device is registered or its actions change.
    /// </summary>
    public event Action? DevicesChanged;

    public bool? IsOnline(string deviceId) => GetPresence(deviceId)?.IsOnline;

    public DevicePresence? GetPresence(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return null;
        }

        lock (state)
        {
            var isOnline = state.ReportedOnline == true ? !state.IsHeartbeatMissed : state.ReportedOnline;
            return new DevicePresence(isOnline, state.LastSeenUtc, GetHeartbeatInterval(state), state.ReportedOnline == true && state.IsHeartbeatMissed);
        }
    }

    public DeviceTelemetry? GetTelemetry(string deviceId, string key)
        => _devices.TryGetValue(deviceId, out var state) && state.Telemetry.TryGetValue(key, out var telemetry) ? telemetry : null;

    public IReadOnlyCollection<string> GetTelemetryKeys(string deviceId)
        => _devices.TryGetValue(deviceId, out var state) ? state.Telemetry.Keys.ToList() : [];

    /// <summary>
    /// The <see cref="DeviceInfo"/> the device last published to <see cref="DeviceTopics.InfoKey"/>, or null when it is missing or not valid.
    /// </summary>
    public DeviceInfo? GetInfo(string deviceId)
    {
        if (GetTelemetry(deviceId, DeviceTopics.InfoKey) is not { } telemetry)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceInfo>(telemetry.Payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SetStatus(string deviceId, bool? isOnline)
    {
        var state = Touch(deviceId, out var changed);
        lock (state)
        {
            if (state.ReportedOnline != isOnline)
            {
                state.ReportedOnline = isOnline;
                changed = true;
            }
        }

        if (changed)
        {
            StatusChanged?.Invoke();
        }
    }

    /// <summary>
    /// Sets the heartbeat interval announced in the twin. Null uses <see cref="DevicePresenceOptions.DefaultHeartbeatInterval"/>.
    /// </summary>
    public void SetHeartbeatInterval(string deviceId, TimeSpan? interval)
    {
        var state = Touch(deviceId, out var changed);
        lock (state)
        {
            state.HeartbeatInterval = interval is { } value && value > TimeSpan.Zero ? value : null;
        }

        if (changed)
        {
            StatusChanged?.Invoke();
        }
    }

    public void SetTelemetry(string deviceId, string key, string? payload)
    {
        var state = Touch(deviceId, out var changed);
        if (string.IsNullOrEmpty(payload))
        {
            state.Telemetry.TryRemove(key, out _);
        }
        else
        {
            state.Telemetry[key] = new DeviceTelemetry(payload, timeProvider.GetUtcNow());
        }

        if (changed)
        {
            StatusChanged?.Invoke();
        }
    }

    /// <summary>
    /// Marks devices that report online but sent nothing for <see cref="DevicePresenceOptions.MissedHeartbeats"/> heartbeat intervals.
    /// </summary>
    /// <returns>Ids of the devices that just missed their heartbeats.</returns>
    public IReadOnlyList<string> CheckHeartbeats()
    {
        var now = timeProvider.GetUtcNow();
        var missed = new List<string>();
        foreach (var (deviceId, state) in _devices)
        {
            lock (state)
            {
                if (state.ReportedOnline != true || state.IsHeartbeatMissed || state.LastSeenUtc is not { } lastSeen)
                {
                    continue;
                }

                if (now - lastSeen > GetHeartbeatInterval(state) * Math.Max(1, options.Value.MissedHeartbeats))
                {
                    state.IsHeartbeatMissed = true;
                    missed.Add(deviceId);
                }
            }
        }

        if (missed.Count > 0)
        {
            StatusChanged?.Invoke();
        }

        return missed;
    }

    public void Remove(string deviceId)
    {
        if (_devices.TryRemove(deviceId, out _))
        {
            StatusChanged?.Invoke();
        }
    }

    public void NotifyDevicesChanged() => DevicesChanged?.Invoke();

    private TimeSpan GetHeartbeatInterval(DeviceState state) => state.HeartbeatInterval ?? options.Value.DefaultHeartbeatInterval;

    /// <summary>
    /// Records that a message of the device arrived. <paramref name="recovered"/> is true when the device had missed its heartbeats.
    /// </summary>
    private DeviceState Touch(string deviceId, out bool recovered)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        lock (state)
        {
            state.LastSeenUtc = timeProvider.GetUtcNow();
            recovered = state.IsHeartbeatMissed;
            state.IsHeartbeatMissed = false;
        }

        return state;
    }

    private sealed class DeviceState
    {
        public bool? ReportedOnline { get; set; }

        public DateTimeOffset? LastSeenUtc { get; set; }

        public TimeSpan? HeartbeatInterval { get; set; }

        public bool IsHeartbeatMissed { get; set; }

        public ConcurrentDictionary<string, DeviceTelemetry> Telemetry { get; } = new(StringComparer.Ordinal);
    }
}
