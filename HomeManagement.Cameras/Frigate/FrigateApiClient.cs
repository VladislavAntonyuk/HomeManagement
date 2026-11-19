using System.Text;
using System.Text.Json;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Frigate HTTP API, see https://docs.frigate.video/integrations/api/frigate-http-api.
/// </summary>
public sealed class FrigateApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Label of the manual events created for on-demand recordings.
    /// </summary>
    public const string ManualEventLabel = "manual";

    public async Task<JsonDocument> GetConfigAsync(CancellationToken token = default)
    {
        await using var stream = await httpClient.GetStreamAsync("api/config", token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    /// <summary>
    /// Creates a manual event, so Frigate records the camera until the event ends or <paramref name="maxDurationSeconds"/> pass.
    /// </summary>
    /// <returns>Id of the event.</returns>
    public async Task<string> CreateManualEventAsync(string camera, int maxDurationSeconds, CancellationToken token = default)
    {
        using var response = await httpClient.PostAsync(
            $"api/events/{Uri.EscapeDataString(camera)}/{ManualEventLabel}/create",
            ToJson(new { sub_label = "Home Management", duration = maxDurationSeconds, include_recording = true }),
            token);
        using var result = await ReadResultAsync(response, token);
        return result.RootElement.TryGetProperty("event_id", out var eventId) && eventId.GetString() is { Length: > 0 } id
            ? id
            : throw new InvalidOperationException("Frigate did not return the id of the event.");
    }

    public async Task EndEventAsync(string eventId, CancellationToken token = default)
    {
        using var response = await httpClient.PutAsync($"api/events/{Uri.EscapeDataString(eventId)}/end", ToJson(new { }), token);
        using var _ = await ReadResultAsync(response, token);
    }

    // A buffered body with a Content-Length, not a chunked stream, so every proxy in front of Frigate passes it on.
    private static StringContent ToJson(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Frigate answers <c>{ "success": false, "message": "..." }</c> when a request fails.
    /// </summary>
    private static async Task<JsonDocument> ReadResultAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        JsonDocument? result = null;
        try
        {
            result = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
        }

        if (response.IsSuccessStatusCode && result is not null)
        {
            return result;
        }

        var message = result?.RootElement.ValueKind == JsonValueKind.Object && result.RootElement.TryGetProperty("message", out var text)
            ? text.GetString()
            : body;
        result?.Dispose();
        throw new HttpRequestException($"Frigate answered {(int)response.StatusCode}: {message}", null, response.StatusCode);
    }
}
