using Microsoft.AspNetCore.Authentication.Cookies;

namespace HomeManagement.Infrastructure.Extensions;

/// <summary>
/// Applies the configured cookie domain only when the request host belongs to it.
/// Requests by any other host (e.g. Tailscale IP) get a host-only cookie.
/// </summary>
public sealed class HostAwareCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager inner = new();

    public string? GetRequestCookie(HttpContext context, string key) => inner.GetRequestCookie(context, key);

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options)
        => inner.AppendResponseCookie(context, key, value, Adjust(context, options));

    public void DeleteCookie(HttpContext context, string key, CookieOptions options)
        => inner.DeleteCookie(context, key, Adjust(context, options));

    private static CookieOptions Adjust(HttpContext context, CookieOptions options)
    {
        if (string.IsNullOrEmpty(options.Domain))
        {
            return options;
        }

        var host = context.Request.Host.Host;
        var domain = options.Domain.TrimStart('.');
        if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        return new CookieOptions(options) { Domain = null };
    }
}
