using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.Router;

public class InternetWatchdogService(
    IRouterController routerController,
    IOptionsMonitor<InternetWatchdogOptions> optionsMonitor,
    ILogger<InternetWatchdogService> logger)
    : BackgroundService
{
    private int _consecutiveFailures;
    private DateTimeOffset _lastRebootAttempt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                await DelayAsync(options, stoppingToken);
                continue;
            }

            var hasInternet = await HasInternetAccessAsync(options, stoppingToken);
            if (hasInternet)
            {
                _consecutiveFailures = 0;
                await DelayAsync(options, stoppingToken);
                continue;
            }

            _consecutiveFailures++;
            logger.LogWarning(
                "Internet check failed ({FailureCount}/{Threshold}) for ping target {PingAddress}.",
                _consecutiveFailures,
                Math.Max(1, options.ConsecutiveFailuresBeforeReboot),
                options.PingAddress);

            if (_consecutiveFailures >= Math.Max(1, options.ConsecutiveFailuresBeforeReboot))
            {
                var rebootCooldown = TimeSpan.FromMinutes(Math.Max(0, options.RebootCooldownMinutes));
                var now = DateTimeOffset.UtcNow;
                if (now - _lastRebootAttempt >= rebootCooldown)
                {
                    _lastRebootAttempt = now;
                    try
                    {
                        var result = await routerController.RebootModem();
                        if (string.IsNullOrEmpty(result.ErrorStatus))
                        {
                            logger.LogWarning("Internet is unavailable. Modem reboot has been requested.");
                            _consecutiveFailures = 0;
                        }
                        else
                        {
                            logger.LogError("Failed to reboot modem: {ErrorStatus}", result.ErrorStatus);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        logger.LogError(ex, "HTTP error while requesting modem reboot.");
                    }
                }
            }

            await DelayAsync(options, stoppingToken);
        }
    }

    private static async Task<bool> HasInternetAccessAsync(InternetWatchdogOptions options, CancellationToken stoppingToken)
    {
        using var ping = new Ping();
        var timeout = TimeSpan.FromMilliseconds(Math.Max(500, options.PingTimeoutMs));

        try
        {
            var reply = await ping.SendPingAsync(options.PingAddress, timeout, cancellationToken: stoppingToken);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    private static Task DelayAsync(InternetWatchdogOptions options, CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, options.CheckIntervalSeconds));
        return Task.Delay(interval, token);
    }
}
