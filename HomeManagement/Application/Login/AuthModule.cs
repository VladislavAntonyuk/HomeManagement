using System.Security.Claims;
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
            var cfg = authOptions.Value;
            if (key == cfg.Key)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, username)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                var props = new AuthenticationProperties
                {
                    IsPersistent = remember,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
                };
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);

                var target = NormalizeReturnUrl(returnUrl);
                return Results.Redirect(target);
            }

            var redirect = "/Account/Login?error=1" + (string.IsNullOrWhiteSpace(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");
            return Results.Redirect(redirect);

            static string NormalizeReturnUrl(string? returnUrl)
            {
                if (string.IsNullOrWhiteSpace(returnUrl))
                {
                    return "/";
                }

                if (Uri.TryCreate(returnUrl, UriKind.Relative, out _))
                {
                    return returnUrl;
                }

                return "/";
            }
        });

        app.MapPost("api/account/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Redirect("/");
        });
    }
}