using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeManagement.Cameras.Frigate;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace HomeManagement.Cameras;

/// <summary>
/// Camera list, snapshots and MJPEG streams for the app, served by the camera module, so the hub never talks to Frigate itself.
/// </summary>
public static partial class CameraEndpoints
{
    private const int DefaultHeight = 720;
    private const int DefaultFps = 5;

    /// <summary>
    /// Maps the camera routes of <see cref="ApiRoutes"/> on a group that the host has already secured.
    /// </summary>
    public static IEndpointRouteBuilder MapCameraApi(this IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Cameras, GetCamerasAsync);
        api.MapGet(ApiRoutes.CameraSnapshot, (string camera, int? height, FrigateApiClient frigate, HttpContext context)
            => ProxyAsync(camera, $"api/{camera}/latest.jpg?h={ClampHeight(height)}", frigate, context));
        api.MapGet(ApiRoutes.CameraStream, (string camera, int? height, int? fps, FrigateApiClient frigate, HttpContext context)
            => ProxyAsync(camera, $"api/{camera}?h={ClampHeight(height)}&fps={Math.Clamp(fps ?? DefaultFps, 1, 30)}", frigate, context));
        return api;
    }

    private static async Task<IResult> GetCamerasAsync(FrigateApiClient frigate, ILoggerFactory loggerFactory, CancellationToken token)
    {
        try
        {
            using var config = await frigate.GetConfigAsync(token);
            List<CameraDto> cameras = config.RootElement.TryGetProperty("cameras", out var element) && element.ValueKind == JsonValueKind.Object
                ? [.. element.EnumerateObject()
                    .Where(x => !x.Value.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False)
                    .Select(x => new CameraDto(x.Name))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)]
                : [];
            return TypedResults.Ok(cameras);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !token.IsCancellationRequested)
        {
            loggerFactory.CreateLogger(nameof(CameraEndpoints)).LogWarning(e, "Failed to read cameras from Frigate.");
            return FrigateUnavailable();
        }
    }

    private static async Task<IResult> ProxyAsync(string camera, string path, FrigateApiClient frigate, HttpContext context)
    {
        if (!CameraName().IsMatch(camera))
        {
            return TypedResults.NotFound();
        }

        HttpResponseMessage response;
        try
        {
            response = await frigate.OpenAsync(path, context.RequestAborted);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            return FrigateUnavailable();
        }

        context.Response.RegisterForDispose(response);
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == HttpStatusCode.NotFound ? TypedResults.NotFound() : FrigateUnavailable();
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
