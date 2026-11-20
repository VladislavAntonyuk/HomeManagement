using System.Collections.Concurrent;
using System.Text.Json;
using HomeManagement.Shared;
using HomeManagement.Shared.Api;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.DeviceManagement;

public sealed record DeviceTelemetry(string Payload, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Latest status and telemetry reported by devices. Rebuilt from retained MQTT messages after every hub start.
/// </summary>
public sealed class DeviceStateStore(TimeProvider timeProvider, IOptions<DevicePresenceOptions> options)
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

    /// <summary>
    /// Raised when a device reports a telemetry value that differs from the previous one.
    /// </summary>
    public event Action? TelemetryChanged;

    public bool IsOnline(string deviceId) => GetPresence(deviceId).IsOnline;

    public DevicePresence GetPresence(string deviceId)
    {
        if (!_devices.TryGetValue(deviceId, out var state))
        {
            return new DevicePresence(DevicePresenceStatus.Unknown, null, options.Value.DefaultHeartbeatInterval);
        }

        lock (state.Sync)
        {
            var status = state.ReportedOnline switch
            {
                true when state.IsHeartbeatMissed => DevicePresenceStatus.NotResponding,
                true => DevicePresenceStatus.Online,
                false => DevicePresenceStatus.Offline,
                null => DevicePresenceStatus.Unknown
            };
            return new DevicePresence(status, state.LastSeenUtc, GetHeartbeatInterval(state));
        }
    }

    public DeviceTelemetry? GetTelemetry(string deviceId, string key)
        => _devices.TryGetValue(deviceId, out var state) && state.Telemetry.TryGetValue(key, out var telemetry) ? telemetry : null;

    public IReadOnlyCollection<string> GetTelemetryKeys(string deviceId)
        => _devices.TryGetValue(deviceId, out var state) ? state.Telemetry.Keys.ToList() : [];

    /// <summary>
    /// The <see cref="DeviceInfo"/> the device last published to <see cref="DeviceTopics.InfoKey"/>, or null when it is missing or not valid.
    /// It is parsed once, when it arrives.
    /// </summary>
    public DeviceInfo? GetInfo(string deviceId) => _devices.TryGetValue(deviceId, out var state) ? state.Info : null;

    public void SetStatus(string deviceId, bool? isOnline)
    {
        var state = Touch(deviceId, out var changed);
        lock (state.Sync)
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
        lock (state.Sync)
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
        bool valueChanged;
        lock (state.Sync)
        {
            if (string.IsNullOrEmpty(payload))
            {
                valueChanged = state.Telemetry.TryRemove(key, out _);
            }
            else
            {
                valueChanged = state.Telemetry.TryGetValue(key, out var current) is false || current.Payload != payload;
                state.Telemetry[key] = new DeviceTelemetry(payload, timeProvider.GetUtcNow());
            }

            if (key == DeviceTopics.InfoKey)
            {
                state.Info = ParseInfo(payload);
            }
        }

        if (changed)
        {
            StatusChanged?.Invoke();
        }

        if (valueChanged)
        {
            TelemetryChanged?.Invoke();
        }
    }

    /// <summary>
    /// Marks devices that report online but sent nothing for <see cref="DevicePresenceOptions.MissedHeartbeats"/> heartbeat intervals.
    /// </summary>
    /// <returns>The devices that just missed their heartbeats, with when the hub last heard from them.</returns>
    public IReadOnlyList<(string DeviceId, DateTimeOffset LastSeenUtc)> CheckHeartbeats()
    {
        var now = timeProvider.GetUtcNow();
        List<(string DeviceId, DateTimeOffset LastSeenUtc)> missed = [];
        foreach (var (deviceId, state) in _devices)
        {
            lock (state.Sync)
            {
                if (state.ReportedOnline != true || state.IsHeartbeatMissed || state.LastSeenUtc is not { } lastSeen)
                {
                    continue;
                }

                if (now - lastSeen > GetHeartbeatInterval(state) * Math.Max(1, options.Value.MissedHeartbeats))
                {
                    state.IsHeartbeatMissed = true;
                    missed.Add((deviceId, lastSeen));
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

    private static DeviceInfo? ParseInfo(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceInfo>(payload, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private TimeSpan GetHeartbeatInterval(DeviceState state) => state.HeartbeatInterval ?? options.Value.DefaultHeartbeatInterval;

    /// <summary>
    /// Records that a message of the device arrived. <paramref name="recovered"/> is true when the device had missed its heartbeats.
    /// </summary>
    private DeviceState Touch(string deviceId, out bool recovered)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState());
        lock (state.Sync)
        {
            state.LastSeenUtc = timeProvider.GetUtcNow();
            recovered = state.IsHeartbeatMissed;
            state.IsHeartbeatMissed = false;
        }

        return state;
    }

    private sealed class DeviceState
    {
        public Lock Sync { get; } = new();

        public bool? ReportedOnline { get; set; }

        public DateTimeOffset? LastSeenUtc { get; set; }

        public TimeSpan? HeartbeatInterval { get; set; }

        public bool IsHeartbeatMissed { get; set; }

        public DeviceInfo? Info { get; set; }

        public ConcurrentDictionary<string, DeviceTelemetry> Telemetry { get; } = new(StringComparer.Ordinal);
    }
}
