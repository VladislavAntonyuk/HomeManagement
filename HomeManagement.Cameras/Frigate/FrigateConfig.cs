using System.Text.Json;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// The parts of the Frigate configuration the camera module uses, read from <c>api/config</c>.
/// </summary>
public sealed record FrigateConfig(IReadOnlyList<FrigateCamera> Cameras, IReadOnlyDictionary<string, string> Profiles)
{
    public static readonly FrigateConfig Empty = new([], new Dictionary<string, string>());

    public static FrigateConfig Read(JsonElement config, int rtmpPort)
    {
        var streamPaths = FrigateCameraStreams.GetRtmpStreamPaths(config, rtmpPort);
        List<FrigateCamera> cameras = config.TryGetProperty("cameras", out var camerasElement) && camerasElement.ValueKind == JsonValueKind.Object
            ? [.. camerasElement.EnumerateObject()
                .Select(x => new FrigateCamera(
                    x.Name,
                    !x.Value.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                    streamPaths.GetValueOrDefault(x.Name) ?? []))
                .OrderBy(x => x.Name, StringComparer.Ordinal)]
            : [];

        var profiles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (config.TryGetProperty("profiles", out var profilesElement) && profilesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var profile in profilesElement.EnumerateObject())
            {
                profiles[profile.Name] = profile.Value.ValueKind == JsonValueKind.Object
                    && profile.Value.TryGetProperty("friendly_name", out var friendlyName)
                    && friendlyName.GetString() is { } name
                    && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : profile.Name;
            }
        }

        return new FrigateConfig(cameras, profiles);
    }

    /// <summary>
    /// True when both have the same cameras and profiles, so the device actions built from them are the same.
    /// </summary>
    public bool HasSameActionsAs(FrigateConfig other)
        => Cameras.Select(x => x.Name).SequenceEqual(other.Cameras.Select(x => x.Name))
           && Profiles.SequenceEqual(other.Profiles);
}

/// <summary>
/// Published in process each time the configuration is read after Frigate came online.
/// </summary>
public sealed record FrigateConfigLoaded(FrigateConfig Config);

/// <param name="RtmpStreamPaths">The streams of this server the camera reads, directly or through go2rtc.</param>
public sealed record FrigateCamera(string Name, bool IsEnabled, IReadOnlySet<string> RtmpStreamPaths);
