using LiveStreamingServerNet.Rtmp.Server.Contracts;
using LiveStreamingServerNet.Utilities.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Enables a Frigate camera while the RTMP stream it reads is published to this server and disables it otherwise.
/// Cameras that do not read from this server are not touched.
/// </summary>
public sealed class FrigateStreamSync(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<CamerasOptions> options,
    ILogger<FrigateStreamSync> logger) : IRtmpServerStreamEventHandler
{
    private readonly HashSet<string> _activeStreams = new(StringComparer.Ordinal);
    private readonly Lock _activeStreamsLock = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public int GetOrder() => 0;

    public ValueTask OnRtmpStreamPublishedAsync(IEventContext context, uint clientId, string streamPath, IReadOnlyDictionary<string, string> streamArguments)
    {
        lock (_activeStreamsLock)
        {
            _activeStreams.Add(FrigateCameraStreams.NormalizeStreamPath(streamPath));
        }

        logger.LogInformation("RTMP stream '{StreamPath}' published.", streamPath);
        _ = SyncAsync();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnRtmpStreamUnpublishedAsync(IEventContext context, uint clientId, string streamPath)
    {
        lock (_activeStreamsLock)
        {
            _activeStreams.Remove(FrigateCameraStreams.NormalizeStreamPath(streamPath));
        }

        logger.LogInformation("RTMP stream '{StreamPath}' unpublished.", streamPath);
        _ = SyncAsync();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnRtmpStreamSubscribedAsync(IEventContext context, uint clientId, string streamPath, IReadOnlyDictionary<string, string> streamArguments)
        => ValueTask.CompletedTask;

    public ValueTask OnRtmpStreamUnsubscribedAsync(IEventContext context, uint clientId, string streamPath)
        => ValueTask.CompletedTask;

    public ValueTask OnRtmpStreamMetaDataReceivedAsync(IEventContext context, uint clientId, string streamPath, IReadOnlyDictionary<string, object> metaData)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Frigate retains its availability, so this also syncs once after every hub start and broker reconnect.
    /// </summary>
    public void OnFrigateAvailability(StringMessage<FrigateAvailabilityPayload> message)
    {
        if (message.Content == FrigateTopics.Online)
        {
            // Not awaited: the consumer of the Frigate client must not wait for its own producer.
            _ = SyncAsync();
        }
    }

    private async Task SyncAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            string[] activeStreams;
            lock (_activeStreamsLock)
            {
                activeStreams = [.. _activeStreams];
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            var cameras = await GetCameraStreamPathsAsync(scope.ServiceProvider.GetRequiredService<FrigateApiClient>());
            foreach (var (camera, streamPaths) in cameras)
            {
                if (streamPaths.Count == 0)
                {
                    continue;
                }

                var isEnabled = streamPaths.Overlaps(activeStreams);
                await publisher.WrapAndPublishAsync(
                    new StringMessage<FrigateCommandPayload>(isEnabled ? FrigateTopics.On : FrigateTopics.Off),
                    envelope => envelope.SetMqttDestinationTopic(FrigateTopics.EnabledSet(camera)));

                logger.LogInformation(
                    "Frigate camera '{Camera}' {State}: stream '{StreamPath}' is {StreamState}.",
                    camera,
                    isEnabled ? "enabled" : "disabled",
                    string.Join(", ", streamPaths),
                    isEnabled ? "published" : "not published");
            }

            var linkedStreams = cameras.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);
            foreach (var streamPath in activeStreams.Where(x => !linkedStreams.Contains(x)))
            {
                logger.LogWarning("RTMP stream '{StreamPath}' is published, but no Frigate camera reads it.", streamPath);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to sync Frigate cameras with RTMP streams.");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<Dictionary<string, HashSet<string>>> GetCameraStreamPathsAsync(FrigateApiClient frigateApiClient)
    {
        using var config = await frigateApiClient.GetConfigAsync();
        return FrigateCameraStreams.GetRtmpStreamPaths(config.RootElement, options.Value.RtmpPort);
    }
}
