using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HomeManagement.Application.Login;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HomeManagement.Infrastructure.Api;

public static class ApiAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string PolicyName = "ApiPolicy";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<StaticAuthOptions> authOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiRoutes.ApiKeyHeader, out var key))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key.ToString()), Encoding.UTF8.GetBytes(authOptions.Value.Key)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "api-client")], ApiAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiAuthenticationDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
