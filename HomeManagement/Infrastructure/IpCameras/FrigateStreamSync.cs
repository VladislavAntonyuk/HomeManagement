using System.Text.Json;
using LiveStreamingServerNet.Rtmp.Server.Contracts;
using LiveStreamingServerNet.Utilities.Contracts;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Infrastructure.IpCameras;

/// <summary>
/// Enables a Frigate camera while the RTMP stream it reads is published to this server and disables it otherwise.
/// Cameras that do not read from this server are not touched.
/// </summary>
public sealed class FrigateStreamSync(
    IServiceScopeFactory serviceScopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<FrigateStreamSync> logger) : IRtmpServerStreamEventHandler
{
    public const string HttpClientName = "frigate";

    private readonly HashSet<string> _activeStreams = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public int GetOrder() => 0;

    public ValueTask OnRtmpStreamPublishedAsync(IEventContext context, uint clientId, string streamPath, IReadOnlyDictionary<string, string> streamArguments)
    {
        lock (_activeStreams)
        {
            _activeStreams.Add(FrigateCameraStreams.NormalizeStreamPath(streamPath));
        }

        logger.LogInformation("RTMP stream '{StreamPath}' published.", streamPath);
        _ = SyncAsync();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnRtmpStreamUnpublishedAsync(IEventContext context, uint clientId, string streamPath)
    {
        lock (_activeStreams)
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
    public void OnFrigateAvailability(StringMessage<FrigateAvailability> message)
    {
        if (message.Content == FrigateAvailability.Online)
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
            var cameras = await GetCameraStreamPathsAsync();
            string[] activeStreams;
            lock (_activeStreams)
            {
                activeStreams = _activeStreams.ToArray();
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            foreach (var (camera, streamPaths) in cameras)
            {
                if (streamPaths.Count == 0)
                {
                    continue;
                }

                var isEnabled = streamPaths.Overlaps(activeStreams);
                await publisher.WrapAndPublishAsync(
                    new StringMessage<FrigateCameraEnabled>(isEnabled ? FrigateCameraEnabled.On : FrigateCameraEnabled.Off),
                    envelope => envelope.SetMqttDestinationTopic(FrigateCameraEnabled.SetTopic(camera)));

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

    private async Task<Dictionary<string, HashSet<string>>> GetCameraStreamPathsAsync()
    {
        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        await using var stream = await httpClient.GetStreamAsync("api/config");
        using var config = await JsonDocument.ParseAsync(stream);
        return FrigateCameraStreams.GetRtmpStreamPaths(config.RootElement);
    }
}
