using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Reads the Frigate configuration once each time Frigate comes online and shares it with the whole module.
/// Frigate restarts to apply a new configuration, so it cannot change while Frigate stays online.
/// </summary>
public sealed class FrigateConfigProvider(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<CamerasOptions> options,
    ILogger<FrigateConfigProvider> logger)
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile FrigateConfig? _current;

    /// <summary>
    /// The configuration of the running Frigate, read now if it was not read since Frigate came online.
    /// </summary>
    public async Task<FrigateConfig> GetAsync(CancellationToken token = default)
        => _current ?? await LoadAsync(reload: false, token);

    /// <summary>
    /// Frigate retains its availability, so this also runs once after every hub start and broker reconnect.
    /// Publishes <see cref="FrigateConfigLoaded"/> in process when the configuration is read.
    /// </summary>
    public void OnFrigateAvailability(StringMessage<FrigateAvailabilityPayload> message)
    {
        if (message.Content != FrigateTopics.Online)
        {
            _current = null;
            return;
        }

        // Not awaited: the consumer of the Frigate client must not wait for an HTTP call.
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var config = await LoadAsync(reload: true, CancellationToken.None);
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(new FrigateConfigLoaded(config));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to read the Frigate configuration.");
        }
    }

    private async Task<FrigateConfig> LoadAsync(bool reload, CancellationToken token)
    {
        await _loadLock.WaitAsync(token);
        try
        {
            if (!reload && _current is { } current)
            {
                return current;
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();
            using var document = await scope.ServiceProvider.GetRequiredService<FrigateApiClient>().GetConfigAsync(token);
            var config = FrigateConfig.Read(document.RootElement, options.Value.RtmpPort);
            _current = config;
            return config;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
