using System.Text.Json;
using System.Text.RegularExpressions;
using HomeManagement.Infrastructure.IpCameras;
using HomeManagement.Shared.Api;

namespace HomeManagement.Infrastructure.Api;

public static partial class CameraApi
{
    private const int DefaultHeight = 720;
    private const int DefaultFps = 5;

    public static void MapCameraApi(this IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Cameras, GetCamerasAsync);
        api.MapGet(ApiRoutes.CameraSnapshot, GetSnapshotAsync);
        api.MapGet(ApiRoutes.CameraStream, GetStreamAsync);
    }

    private static async Task<IResult> GetCamerasAsync(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, CancellationToken token)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient(FrigateStreamSync.HttpClientName);
            await using var stream = await httpClient.GetStreamAsync("api/config", token);
            using var config = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var cameras = config.RootElement.TryGetProperty("cameras", out var element) && element.ValueKind == JsonValueKind.Object
                ? element.EnumerateObject()
                    .Where(x => !x.Value.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False)
                    .Select(x => new CameraDto(x.Name))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];
            return TypedResults.Ok(cameras);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
        {
            loggerFactory.CreateLogger(nameof(CameraApi)).LogWarning(e, "Failed to read cameras from Frigate.");
            return FrigateUnavailable();
        }
    }

    private static Task<IResult> GetSnapshotAsync(string camera, int? height, IHttpClientFactory httpClientFactory, HttpContext context)
        => ProxyAsync(camera, $"api/{camera}/latest.jpg?h={ClampHeight(height)}", httpClientFactory, context);

    private static Task<IResult> GetStreamAsync(string camera, int? height, int? fps, IHttpClientFactory httpClientFactory, HttpContext context)
        => ProxyAsync(camera, $"api/{camera}?h={ClampHeight(height)}&fps={Math.Clamp(fps ?? DefaultFps, 1, 30)}", httpClientFactory, context);

    private static async Task<IResult> ProxyAsync(string camera, string path, IHttpClientFactory httpClientFactory, HttpContext context)
    {
        if (!CameraName().IsMatch(camera))
        {
            return TypedResults.NotFound();
        }

        var httpClient = httpClientFactory.CreateClient(FrigateStreamSync.HttpClientName);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        context.Response.RegisterForDispose(httpClient);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            return FrigateUnavailable();
        }

        context.Response.RegisterForDispose(response);
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == System.Net.HttpStatusCode.NotFound ? TypedResults.NotFound() : FrigateUnavailable();
        }

        context.Response.Headers.CacheControl = "no-store";
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return TypedResults.Stream(await response.Content.ReadAsStreamAsync(context.RequestAborted), contentType);
    }

    private static int ClampHeight(int? height) => Math.Clamp(height ?? DefaultHeight, 120, 2160);

    private static IResult FrigateUnavailable()
        => TypedResults.Problem("Frigate is not available.", statusCode: StatusCodes.Status502BadGateway);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex CameraName();
}
