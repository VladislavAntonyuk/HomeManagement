using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HomeManagement.Infrastructure.Api;

public static class ApiAuthenticationDefaults
{
    /// <summary>
    /// The REST API used by the app, with the Auth:Key key.
    /// </summary>
    public const string AuthenticationScheme = "ApiKey";
    public const string PolicyName = "ApiPolicy";

    /// <summary>
    /// The MCP endpoint used by AI agents, with its own Auth:McpKey key.
    /// </summary>
    public const string McpAuthenticationScheme = "McpApiKey";
    public const string McpPolicyName = "McpApiKeyPolicy";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Name of the signed in client, e.g. in logs.
    /// </summary>
    public string ClientName { get; set; } = "api-client";
}

/// <summary>
/// Signs in requests whose <see cref="ApiRoutes.ApiKeyHeader"/> header holds the configured key. Requests without the header are left
/// to the other schemes, e.g. the sign-in cookie.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiRoutes.ApiKeyHeader, out var key))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!IsKey(key.ToString(), Options.Key))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Options.ClientName), new Claim(ClaimTypes.Name, Options.ClientName)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>
    /// Compares in constant time, so the response time does not tell how much of a guessed key is right.
    /// </summary>
    public static bool IsKey(string provided, string expected)
        => expected.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
