using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.Router;

public class AsusRouterController(IHttpClientFactory httpClientFactory, IOptions<RouterOptions> options) : IRouterController
{
    public class LoginResult
    {
        public string? AsusToken { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerOptions.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private HttpClient CreateClient()
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(options.Value.IpAddress);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "asusrouter-Android-DUTUtil-1.0.0.3.58-163");
        return httpClient;
    }

    private async Task<string?> AuthenticateAsync(HttpClient httpClient)
    {
        const string path = "/login.cgi";
        var formattedUsernamePassword = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Value.Username}:{options.Value.Password}"));

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "login_authorization", formattedUsernamePassword }
        });

        var response = await httpClient.PostAsync(path, content);
        response.EnsureSuccessStatusCode();

        var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>(Options);
        return loginResult?.AsusToken;
    }

    private async Task<RouterResponse> ApplyAppPostAsync(Dictionary<string, string> payload)
    {
        var httpClient = CreateClient();
        var asusToken = await AuthenticateAsync(httpClient);
        if (string.IsNullOrEmpty(asusToken))
        {
            return new RouterResponse { ErrorStatus = "Authentication failed" };
        }

        const string path = "/apply.cgi";
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(payload)
        };
        request.Headers.Add("Cookie", $"asus_token={asusToken}");

        var response = await httpClient.SendAsync(request);
        return new RouterResponse { ErrorStatus = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync() };
    }

    public Task<RouterResponse> SetLeds(bool enabled)
    {
        var configJson = JsonSerializer.Serialize(new { led_val = enabled ? 1 : 0 });
        return ApplyAppPostAsync(new Dictionary<string, string>
        {
            { "config", configJson },
            { "action_mode", "config_changed" }
        });
    }

    public Task<RouterResponse> RebootModem()
    {
        return ApplyAppPostAsync(new Dictionary<string, string>
        {
            { "action_mode", "rebootmodem" }
        });
    }

    public Task<RouterResponse> Reboot()
    {
        return ApplyAppPostAsync(new Dictionary<string, string>
        {
            { "action_mode", "reboot" }
        });
    }
}
