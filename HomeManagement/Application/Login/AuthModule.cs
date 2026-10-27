using System.Security.Claims;
using HomeManagement.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.Login;

public static class AuthModule
{
    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/login", async (
            HttpContext context,
            IOptions<StaticAuthOptions> authOptions,
            [FromForm] string username,
            [FromForm] string key,
            [FromForm] bool remember,
            [FromForm] string? returnUrl) =>
        {
            if (key == authOptions.Value.Key)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, username)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                var props = new AuthenticationProperties
                {
                    IsPersistent = remember
                };
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

                return Results.Redirect(NormalizeReturnUrl(returnUrl));
            }

            var redirect = $"/Account/Login?error=1&username={Uri.EscapeDataString(username)}"
                           + (string.IsNullOrWhiteSpace(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");
            return Results.Redirect(redirect);
        });

        app.MapPost("api/account/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Redirect("/");
        });
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        // Local path only: "//host" and "/\host" are treated by browsers as another site
        if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\"))
        {
            return returnUrl;
        }

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && AppHosts.IsMainHostOrSubdomain(uri.Host))
        {
            return uri.ToString();
        }

        return "/";
    }
}
