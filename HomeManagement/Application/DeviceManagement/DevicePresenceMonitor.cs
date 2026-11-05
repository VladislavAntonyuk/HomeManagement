using Microsoft.Extensions.Options;

namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// Shows a device as offline when it reports online but stopped sending heartbeats.
/// The broker only publishes the last will when it notices the disconnect, which it does not when it lost power together with the device.
/// </summary>
public sealed class DevicePresenceMonitor(
    DeviceStateStore stateStore,
    DeviceHub deviceHub,
    IOptions<DevicePresenceOptions> options,
    TimeProvider timeProvider,
    ILogger<DevicePresenceMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CheckInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var deviceId in stateStore.CheckHeartbeats())
            {
                var lastSeen = stateStore.GetPresence(deviceId)?.LastSeenUtc;
                logger.LogWarning("Device '{DeviceId}' reports online, but sent nothing since {LastSeenUtc}. Showing it as offline.", deviceId, lastSeen);
                deviceHub.FailPendingCommands(deviceId, $"Device '{deviceId}' stopped responding.");
            }
        }
    }
}
