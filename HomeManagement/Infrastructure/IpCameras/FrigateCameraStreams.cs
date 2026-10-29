using System.Text.Json;

namespace HomeManagement.Infrastructure.IpCameras;

/// <summary>
/// Finds which RTMP streams each Frigate camera reads, from the configuration returned by the Frigate API.
/// </summary>
public static class FrigateCameraStreams
{
    private const int RtmpPort = 1935;
    private const int Go2RtcRtspPort = 8554;

    /// <summary>
    /// Maps each camera to the RTMP stream paths it reads, directly or through a go2rtc restream.
    /// </summary>
    public static Dictionary<string, HashSet<string>> GetRtmpStreamPaths(JsonElement config)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!config.TryGetProperty("cameras", out var cameras) || cameras.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var go2RtcStreams = ReadGo2RtcStreams(config);
        foreach (var camera in cameras.EnumerateObject())
        {
            var streamPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in ReadInputs(camera.Value))
            {
                AddRtmpStreamPath(streamPaths, input);
                if (TryGetGo2RtcStreamName(input, out var name) && go2RtcStreams.TryGetValue(name, out var sources))
                {
                    foreach (var source in sources)
                    {
                        AddRtmpStreamPath(streamPaths, source);
                    }
                }
            }

            result[camera.Name] = streamPaths;
        }

        return result;
    }

    public static string NormalizeStreamPath(string streamPath) => streamPath.Trim('/');

    private static IEnumerable<string> ReadInputs(JsonElement camera)
    {
        if (!camera.TryGetProperty("ffmpeg", out var ffmpeg)
            || !ffmpeg.TryGetProperty("inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var input in inputs.EnumerateArray())
        {
            if (input.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
            {
                yield return path.GetString()!;
            }
        }
    }

    private static Dictionary<string, List<string>> ReadGo2RtcStreams(JsonElement config)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!config.TryGetProperty("go2rtc", out var go2Rtc)
            || go2Rtc.ValueKind != JsonValueKind.Object
            || !go2Rtc.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var stream in streams.EnumerateObject())
        {
            result[stream.Name] = stream.Value.ValueKind switch
            {
                JsonValueKind.String => [stream.Value.GetString()!],
                JsonValueKind.Array => stream.Value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList(),
                _ => []
            };
        }

        return result;
    }

    private static bool TryGetGo2RtcStreamName(string input, out string name)
    {
        name = string.Empty;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme != "rtsp"
            || !uri.IsLoopback
            || uri.Port != Go2RtcRtspPort)
        {
            return false;
        }

        name = NormalizeStreamPath(uri.AbsolutePath);
        return name.Length > 0;
    }

    private static void AddRtmpStreamPath(HashSet<string> streamPaths, string source)
    {
        // go2rtc sources may look like "ffmpeg:rtmp://host/live/stream#video=copy".
        var url = source.StartsWith("ffmpeg:", StringComparison.Ordinal) ? source["ffmpeg:".Length..] : source;
        var fragmentIndex = url.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            url = url[..fragmentIndex];
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == "rtmp"
            && uri.Port is -1 or RtmpPort
            && NormalizeStreamPath(uri.AbsolutePath) is { Length: > 0 } streamPath)
        {
            streamPaths.Add(streamPath);
        }
    }
}
