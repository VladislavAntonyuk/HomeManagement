using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using HomeManagement.DeviceSdk;
using HomeManagement.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Configuration.Mqtt;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Exposes Frigate as a device: its state and detections as telemetry, and recording and profile switches as commands.
/// The hub sees it like any other device and never talks to Frigate itself.
/// </summary>
public sealed class FrigateDeviceHandler(
    IServiceScopeFactory serviceScopeFactory,
    DeviceTelemetryPublisher telemetryPublisher,
    IOptions<CamerasOptions> options,
    ILogger<FrigateDeviceHandler> logger) : IDeviceHandler
{
    public const string StateKey = "state";
    public const string DetectionKey = "detection";

    private const string CameraPrefix = "cameras/";
    private const string ProfilePrefix = "profile/";
    private const string RecordingOn = "/recording/on";
    private const string RecordingOff = "/recording/off";
    private const string ClipStart = "/clip/start";
    private const string ClipStop = "/clip/stop";

    private readonly ConcurrentDictionary<string, CameraState> _cameraStates = new(StringComparer.Ordinal);

    // Manual events of the on-demand recordings in progress, by camera.
    private readonly ConcurrentDictionary<string, string> _clips = new(StringComparer.Ordinal);

    // Two Start Clip commands at once must not create two Frigate events.
    private readonly SemaphoreSlim _clipLock = new(1, 1);

    // Completes when the cameras and profiles are read, or Frigate is known to be offline.
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile FrigateConfig _config = FrigateConfig.Empty;
    private volatile FrigateStatsMessage? _stats;
    private bool? _isAvailable;
    private string? _profile;

    public event Action? ActionsChanged;

    public IReadOnlyList<DeviceAction> GetActions()
    {
        var config = _config;
        var actions = new List<DeviceAction>
        {
            new("State", CommandType.Get, StateKey),
            new("Detection", CommandType.Event, DetectionKey)
        };

        foreach (var camera in config.Cameras.Select(x => x.Name))
        {
            actions.Add(new($"{camera} State", CommandType.Get, CameraKey(camera)));
            actions.Add(new($"Start Recording {camera}", CommandType.Post, CameraKey(camera) + RecordingOn));
            actions.Add(new($"Stop Recording {camera}", CommandType.Post, CameraKey(camera) + RecordingOff));
            actions.Add(new($"Start Clip {camera}", CommandType.Post, CameraKey(camera) + ClipStart));
            actions.Add(new($"Stop Clip {camera}", CommandType.Post, CameraKey(camera) + ClipStop));
        }

        foreach (var (profile, friendlyName) in config.Profiles)
        {
            actions.Add(new($"Profile {friendlyName}", CommandType.Post, ProfilePrefix + profile));
        }

        if (config.Profiles.Count > 0)
        {
            actions.Add(new("Profile None", CommandType.Post, ProfilePrefix + FrigateTopics.NoProfile));
        }

        return actions;
    }

    public async Task<CommandResponse> ExecuteAsync(string command, CancellationToken token)
    {
        if (command.StartsWith(ProfilePrefix, StringComparison.Ordinal))
        {
            return await SendAsync(FrigateTopics.ProfileSet, command[ProfilePrefix.Length..], token);
        }

        if (TryGetCamera(command, RecordingOn, out var camera))
        {
            return await SendAsync(FrigateTopics.RecordingsSet(camera), FrigateTopics.On, token);
        }

        if (TryGetCamera(command, RecordingOff, out camera))
        {
            return await SendAsync(FrigateTopics.RecordingsSet(camera), FrigateTopics.Off, token);
        }

        if (TryGetCamera(command, ClipStart, out camera))
        {
            return await StartClipAsync(camera, token);
        }

        if (TryGetCamera(command, ClipStop, out camera))
        {
            return await StopClipAsync(camera, token);
        }

        return new CommandResponse(false, $"Unknown command '{command}'.");
    }

    public Task<string?> ReadAsync(string key, CancellationToken token)
    {
        if (key == StateKey)
        {
            return Task.FromResult<string?>(SerializeState());
        }

        return Task.FromResult(
            key.StartsWith(CameraPrefix, StringComparison.Ordinal) && key[CameraPrefix.Length..] is var camera && IsKnownCamera(camera)
                ? SerializeCamera(camera)
                : null);
    }

    public Task WaitUntilReadyAsync(CancellationToken token) => _ready.Task.WaitAsync(token);

    /// <summary>
    /// The host people open Frigate at. The module runs in the hub, so the machine's own address would be the hub's, and Frigate's stats carry none.
    /// </summary>
    public string? GetAddress()
        => Uri.TryCreate(options.Value.FrigatePublicUrl, UriKind.Absolute, out var url) ? url.Host : null;

    /// <summary>
    /// Metrics of Frigate from its latest stats, not of the hub machine the module runs on.
    /// Only the camera count until the first stats arrive, and while Frigate is offline.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetMetrics()
    {
        var metrics = new Dictionary<string, string>
        {
            ["Cameras"] = _config.Cameras.Count.ToString(CultureInfo.InvariantCulture)
        };
        if (_stats is not { } stats)
        {
            return metrics;
        }

        if (stats.Service is { } service)
        {
            metrics["UptimeSeconds"] = service.Uptime.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(service.Version))
            {
                metrics["Version"] = service.Version;
            }
        }

        // The hottest sensor: GPU and NPU usually share one die, and a Coral matters as much as the board.
        if (stats.GetTemperatures().DefaultIfEmpty(double.NaN).Max() is var temperature && !double.IsNaN(temperature))
        {
            metrics["Temperature"] = temperature.ToString(CultureInfo.InvariantCulture);
        }

        return metrics;
    }

    public void OnStats(FrigateStatsMessage message) => _stats = message;

    public async Task OnAvailabilityAsync(StringMessage<FrigateAvailabilityPayload> message)
    {
        _isAvailable = message.Content == FrigateTopics.Online;
        if (_isAvailable == false)
        {
            // Stats of a stopped Frigate would show it as still running.
            _stats = null;
        }

        await telemetryPublisher.TryPublishAsync(StateKey, SerializeState());
        if (_isAvailable == false)
        {
            // Nothing to wait for: the actions are read when Frigate is back online.
            _ready.TrySetResult();
        }
    }

    public async Task OnStateAsync(IInboundEnvelope<StringMessage<FrigateStatePayload>> envelope)
    {
        if (envelope.Endpoint is not MqttConsumerEndpoint endpoint || envelope.Message?.Content is not { } value)
        {
            return;
        }

        if (endpoint.Topic == FrigateTopics.ProfileState)
        {
            _profile = value;
            await telemetryPublisher.TryPublishAsync(StateKey, SerializeState());
            return;
        }

        if (!FrigateTopics.TryParseCameraState(endpoint.Topic, out var camera, out var feature))
        {
            return;
        }

        var isOn = value == FrigateTopics.On;
        _cameraStates.AddOrUpdate(
            camera,
            _ => feature == "recordings" ? new CameraState(null, isOn) : new CameraState(isOn, null),
            (_, current) => feature == "recordings" ? current with { Recording = isOn } : current with { Enabled = isOn });
        await PublishCameraAsync(camera);
    }

    public async Task OnEventAsync(FrigateEventMessage message)
    {
        if (message.Type != "new" || message.After is not { } tracked || string.IsNullOrEmpty(tracked.Id))
        {
            return;
        }

        var publicUrl = options.Value.FrigatePublicUrl.TrimEnd('/');
        var detection = new Detection(
            tracked.Id,
            tracked.Camera,
            tracked.Label,
            Math.Round(tracked.TopScore, 2),
            tracked.HasSnapshot
                ? $"{publicUrl}/api/events/{tracked.Id}/snapshot.jpg?bbox=1"
                : $"{publicUrl}/api/{tracked.Camera}/latest.jpg");

        logger.LogInformation("Frigate detected '{Label}' on camera '{Camera}'.", tracked.Label, tracked.Camera);
        await telemetryPublisher.TryPublishEventAsync(DetectionKey, JsonSerializer.Serialize(detection, JsonSerializerOptions.Web));
    }

    /// <summary>
    /// Rebuilds the actions from the cameras and profiles of the Frigate that just came online.
    /// </summary>
    public void OnConfigLoaded(FrigateConfig config)
    {
        var isChanged = !config.HasSameActionsAs(_config);
        _config = config;
        _ready.TrySetResult();
        if (!isChanged)
        {
            return;
        }

        logger.LogInformation(
            "Frigate cameras: {Cameras}. Profiles: {Profiles}.",
            string.Join(", ", config.Cameras.Select(x => x.Name)),
            string.Join(", ", config.Profiles.Keys));
        ActionsChanged?.Invoke();
    }

    private async Task<CommandResponse> SendAsync(string topic, string payload, CancellationToken token)
    {
        if (_isAvailable == false)
        {
            return new CommandResponse(false, "Frigate is offline.");
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().WrapAndPublishAsync(
            new StringMessage<FrigateCommandPayload>(payload),
            envelope => envelope.SetMqttDestinationTopic(topic),
            token);
        return new CommandResponse(true, JsonSerializer.Serialize(new { topic, payload }, JsonSerializerOptions.Web));
    }

    private async Task<CommandResponse> StartClipAsync(string camera, CancellationToken token)
    {
        await _clipLock.WaitAsync(token);
        try
        {
            return await StartClipCoreAsync(camera, token);
        }
        finally
        {
            _clipLock.Release();
        }
    }

    private async Task<CommandResponse> StopClipAsync(string camera, CancellationToken token)
    {
        await _clipLock.WaitAsync(token);
        try
        {
            return await StopClipCoreAsync(camera, token);
        }
        finally
        {
            _clipLock.Release();
        }
    }

    private async Task<CommandResponse> StartClipCoreAsync(string camera, CancellationToken token)
    {
        if (_clips.TryGetValue(camera, out var current))
        {
            return new CommandResponse(true, JsonSerializer.Serialize(new { camera, eventId = current, alreadyRecording = true }, JsonSerializerOptions.Web));
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var eventId = await scope.ServiceProvider.GetRequiredService<FrigateApiClient>()
            .CreateManualEventAsync(camera, Math.Max(1, options.Value.ClipMaxSeconds), token);
        _clips[camera] = eventId;
        logger.LogInformation("Clip of camera '{Camera}' started as Frigate event '{EventId}'.", camera, eventId);
        await PublishCameraAsync(camera);
        return new CommandResponse(true, JsonSerializer.Serialize(new { camera, eventId }, JsonSerializerOptions.Web));
    }

    private async Task<CommandResponse> StopClipCoreAsync(string camera, CancellationToken token)
    {
        if (!_clips.TryRemove(camera, out var eventId))
        {
            return new CommandResponse(false, $"No clip of camera '{camera}' is recording.");
        }

        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FrigateApiClient>().EndEventAsync(eventId, token);
        }
        catch
        {
            // Keep the clip, so Stop Clip can be tried again.
            _clips.TryAdd(camera, eventId);
            throw;
        }

        logger.LogInformation("Clip of camera '{Camera}' (Frigate event '{EventId}') stopped.", camera, eventId);
        await PublishCameraAsync(camera);
        return new CommandResponse(true, JsonSerializer.Serialize(new { camera, eventId }, JsonSerializerOptions.Web));
    }

    // Always published, also when nothing is known but the end of a clip, so the hub never keeps a stale value.
    private Task PublishCameraAsync(string camera) => telemetryPublisher.TryPublishAsync(CameraKey(camera), SerializeCamera(camera));

    private bool IsKnownCamera(string camera)
        => _config.Cameras.Any(x => x.Name == camera) || _cameraStates.ContainsKey(camera) || _clips.ContainsKey(camera);

    private string SerializeCamera(string camera)
    {
        _cameraStates.TryGetValue(camera, out var state);
        var hasClip = _clips.TryGetValue(camera, out var clipEventId);
        return JsonSerializer.Serialize(new { enabled = state?.Enabled, recording = state?.Recording, clip = hasClip, clipEventId }, JsonSerializerOptions.Web);
    }

    private static bool TryGetCamera(string command, string suffix, out string camera)
    {
        if (command.StartsWith(CameraPrefix, StringComparison.Ordinal)
            && command.EndsWith(suffix, StringComparison.Ordinal)
            && command.Length > CameraPrefix.Length + suffix.Length)
        {
            camera = command[CameraPrefix.Length..^suffix.Length];
            return true;
        }

        camera = string.Empty;
        return false;
    }

    private string SerializeState() => JsonSerializer.Serialize(new { available = _isAvailable, profile = _profile }, JsonSerializerOptions.Web);

    private static string CameraKey(string camera) => CameraPrefix + camera;

    private sealed record CameraState(bool? Enabled, bool? Recording);

    private sealed record Detection(string Id, string Camera, string? Label, double Score, string SnapshotUrl);
}
