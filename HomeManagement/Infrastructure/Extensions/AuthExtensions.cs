using HomeManagement.Application.Login;
using HomeManagement.Infrastructure.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HomeManagement.Infrastructure.Extensions;

public static class AuthExtensions
{
    public static void AddAuth(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<StaticAuthOptions>(builder.Configuration.GetSection("Auth"));

        const string mainHost = AppHosts.MainHost;
        // Cookie authentication for Blazor UI + API key authentication for MCP
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/Account/Login";
                o.LogoutPath = "/Account/Logout";
                o.AccessDeniedPath = "/Account/Login";
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);

                o.Cookie.Name = "HomeManagement_Auth";
                // Allows the session cookie to be valid for both home-management.local and cameras.home-management.local
                o.Cookie.Domain = $".{mainHost}";
                // Drops the cookie domain when the app is opened by another host (e.g. Tailscale IP),
                // otherwise the browser rejects the cookie and login never succeeds
                o.CookieManager = new HostAwareCookieManager();
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                o.Events.OnRedirectToLogin = context =>
                {
                    // If it is an API request, return 401 Unauthorized instead of redirecting
                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Headers.Accept.ToString().Contains("application/json"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    // Redirect to the main host login page if challenged on a subdomain
                    if (context.Request.Host.Host.EndsWith($".{mainHost}", StringComparison.OrdinalIgnoreCase))
                    {
                        var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                        var loginUri = $"{context.Request.Scheme}://{mainHost}/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";

                        context.Response.Redirect(loginUri);
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, McpApiKeyAuthenticationHandler>(
                McpApiKeyAuthenticationDefaults.AuthenticationScheme,
                _ => { });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(McpApiKeyAuthenticationDefaults.PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(McpApiKeyAuthenticationDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        builder.Services.AddCascadingAuthenticationState();
    }
}