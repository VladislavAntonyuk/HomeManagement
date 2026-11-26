using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.Router;

/// <summary>
/// Talks to the Asus router web API, the way the Asus router app does. Registered as a typed <see cref="HttpClient"/>.
/// </summary>
public class AsusRouterController(HttpClient httpClient, IOptions<RouterOptions> options) : IRouterController
{
    public const string UserAgent = "asusrouter-Android-DUTUtil-1.0.0.3.58-163";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task<OperationResult> SetLedsAsync(bool enabled, CancellationToken token = default)
        => ApplyAsync([new("config", JsonSerializer.Serialize(new { led_val = enabled ? 1 : 0 })), new("action_mode", "config_changed")], token);

    public Task<OperationResult> RebootModemAsync(CancellationToken token = default) => ApplyAsync([new("action_mode", "rebootmodem")], token);

    public Task<OperationResult> RebootAsync(CancellationToken token = default) => ApplyAsync([new("action_mode", "reboot")], token);

    private async Task<OperationResult> ApplyAsync(IEnumerable<KeyValuePair<string, string>> payload, CancellationToken token)
    {
        if (httpClient.BaseAddress is null)
        {
            return OperationResult.Failure(Strings.Router_NotConfigured);
        }

        if (await AuthenticateAsync(token) is not { Length: > 0 } asusToken)
        {
            return OperationResult.Failure(Strings.Router_AuthFailed);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/apply.cgi");
        request.Content = new FormUrlEncodedContent(payload);
        request.Headers.Add("Cookie", $"asus_token={asusToken}");

        using var response = await httpClient.SendAsync(request, token);
        return response.IsSuccessStatusCode
            ? OperationResult.Success("Done")
            : OperationResult.Failure(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<string?> AuthenticateAsync(CancellationToken token)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.Username}:{options.Value.Password}"));
        using var content = new FormUrlEncodedContent([new("login_authorization", credentials)]);
        using var response = await httpClient.PostAsync("/login.cgi", content, token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, token))?.AsusToken;
    }

    private sealed record LoginResult(string? AsusToken);
}
