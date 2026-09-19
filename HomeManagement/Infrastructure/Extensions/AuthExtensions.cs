using HomeManagement.Application.Login;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HomeManagement.Infrastructure.Extensions;

public static class AuthExtensions
{
    public static void AddAuth(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<StaticAuthOptions>(builder.Configuration.GetSection("Auth"));

        List<string> allowedHosts = ["localhost", "home-management.local"];
        // Cookie authentication for Blazor UI + API key authentication for MCP
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/Account/Login";
                o.LogoutPath = "/Account/Logout";
                o.AccessDeniedPath = "/Account/Login";
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromHours(12);

                o.Cookie.Name = "HomeManagement_Auth";
                // Allows the session cookie to be valid for both home-management.local and cameras.home-management.local
                o.Cookie.Domain =
#if DEBUG
                    "localhost";
#else
                    ".home-management.local";
#endif
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
                    if (!allowedHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
                    {
                        var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
                        var loginUri = $"{context.Request.Scheme}://home-management.local/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}";

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