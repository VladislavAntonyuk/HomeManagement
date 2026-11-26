using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HomeManagement.Devices.TuyaPlug;

/// <summary>
/// Tuya Cloud OpenAPI client: signs every request with the project's Access Secret and keeps the access token until it expires.
/// </summary>
public sealed class TuyaCloudClient(HttpClient httpClient, IOptions<TuyaOptions> options, TimeProvider timeProvider)
{
    // https://developer.tuya.com/en/docs/iot/error-code
    private const int TokenInvalidCode = 1010;

    // Gets a new token this long before the old one expires, so a request never carries an expired token.
    private static readonly TimeSpan TokenRenewalMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private TuyaToken? _token;

    /// <summary>
    /// Whether the device is connected to the Tuya cloud, and its data points, e.g. switch_1 = true.
    /// </summary>
    public async Task<TuyaDevice> GetDeviceAsync(string deviceId, CancellationToken token)
    {
        var result = await SendAsync(HttpMethod.Get, $"/v1.0/devices/{Uri.EscapeDataString(deviceId)}", body: null, token);
        return result.Deserialize(TuyaJsonContext.Default.TuyaDevice) ?? throw new TuyaException("The Tuya cloud returned no device.");
    }

    /// <summary>
    /// Sets a data point of the device, e.g. switch_1 to true.
    /// </summary>
    public async Task SendCommandAsync(string deviceId, string code, JsonNode value, CancellationToken token)
    {
        JsonObject body = new()
        {
            ["commands"] = new JsonArray(new JsonObject { ["code"] = code, ["value"] = value })
        };

        var result = await SendAsync(HttpMethod.Post, $"/v1.0/iot-03/devices/{Uri.EscapeDataString(deviceId)}/commands", body.ToJsonString(), token);
        if (result?.GetValueKind() != JsonValueKind.True)
        {
            throw new TuyaException($"The Tuya cloud did not accept the command: {result?.ToJsonString()}.");
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, string? body, CancellationToken token)
    {
        var accessToken = await GetAccessTokenAsync(token);
        var response = await SendSignedAsync(method, path, body, accessToken, token);
        if (response.Code == TokenInvalidCode)
        {
            // Tuya revokes a token when another client of the project gets a new one.
            await InvalidateTokenAsync(accessToken, token);
            response = await SendSignedAsync(method, path, body, await GetAccessTokenAsync(token), token);
        }

        return response.Success ? response.Result : throw new TuyaException(response);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken token)
    {
        await _tokenLock.WaitAsync(token);
        try
        {
            if (_token is not null && _token.ExpiresUtc - TokenRenewalMargin > timeProvider.GetUtcNow())
            {
                return _token.AccessToken;
            }

            var response = await SendSignedAsync(HttpMethod.Get, "/v1.0/token?grant_type=1", body: null, accessToken: null, token);
            var result = response.Success
                ? response.Result.Deserialize(TuyaJsonContext.Default.TuyaTokenResult) ?? throw new TuyaException("The Tuya cloud returned no token.")
                : throw new TuyaException(response);

            _token = new TuyaToken(result.AccessToken, timeProvider.GetUtcNow().AddSeconds(result.ExpireTime));
            return _token.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task InvalidateTokenAsync(string accessToken, CancellationToken token)
    {
        await _tokenLock.WaitAsync(token);
        try
        {
            // Another request may already have got a new token.
            if (_token?.AccessToken == accessToken)
            {
                _token = null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<TuyaResponse> SendSignedAsync(HttpMethod method, string pathAndQuery, string? body, string? accessToken, CancellationToken token)
    {
        var settings = options.Value;
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");

        using var request = new HttpRequestMessage(method, new Uri(settings.BaseUrl, pathAndQuery));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("client_id", settings.AccessId);
        request.Headers.Add("t", timestamp);
        request.Headers.Add("nonce", nonce);
        request.Headers.Add("sign_method", "HMAC-SHA256");
        request.Headers.Add("sign", Sign(settings, method, pathAndQuery, body, accessToken, timestamp, nonce));
        if (accessToken is not null)
        {
            request.Headers.Add("access_token", accessToken);
        }

        using var response = await httpClient.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(TuyaJsonContext.Default.TuyaResponse, token)
            ?? throw new TuyaException("The Tuya cloud returned an empty response.");
    }

    /// <summary>
    /// https://developer.tuya.com/en/docs/iot/new-singnature
    /// </summary>
    internal static string Sign(TuyaOptions settings, HttpMethod method, string pathAndQuery, string? body, string? accessToken, string timestamp, string nonce)
    {
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty)));

        // No signed headers, so their line is empty.
        var stringToSign = $"{method.Method}\n{contentHash}\n\n{SortQuery(pathAndQuery)}";
        var message = settings.AccessId + accessToken + timestamp + nonce + stringToSign;

        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.AccessSecret), Encoding.UTF8.GetBytes(message)));
    }

    private static string SortQuery(string pathAndQuery)
    {
        var index = pathAndQuery.IndexOf('?');
        if (index < 0)
        {
            return pathAndQuery;
        }

        var parameters = pathAndQuery[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        return $"{pathAndQuery[..index]}?{string.Join('&', parameters)}";
    }

    private sealed record TuyaToken(string AccessToken, DateTimeOffset ExpiresUtc);
}

public sealed record TuyaTokenResult(string AccessToken, int ExpireTime);

public sealed record TuyaResponse(bool Success, int Code, string? Msg, JsonNode? Result);

public sealed record TuyaDevice(string Id, string? Name, bool Online, IReadOnlyList<TuyaDataPoint>? Status);

public sealed record TuyaDataPoint(string Code, JsonNode? Value);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(TuyaResponse))]
[JsonSerializable(typeof(TuyaTokenResult))]
[JsonSerializable(typeof(TuyaDevice))]
internal sealed partial class TuyaJsonContext : JsonSerializerContext;

public sealed class TuyaException : Exception
{
    public TuyaException(string message)
        : base(message)
    {
    }

    public TuyaException(TuyaResponse response)
        : base($"Tuya cloud error {response.Code}: {response.Msg}")
    {
    }
}
