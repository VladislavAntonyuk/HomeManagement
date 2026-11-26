using System.Globalization;
using HomeManagement.Infrastructure.Localization;
using Microsoft.AspNetCore.Localization;
using MudBlazor;

namespace HomeManagement.Infrastructure.Extensions;

public static class LocalizationExtensions
{
    /// <summary>
    /// The cookie that keeps the language the user chose, read before the browser's Accept-Language.
    /// </summary>
    public static readonly string CultureCookieName = CookieRequestCultureProvider.DefaultCookieName;

    public static void AddLocalizationSupport(this WebApplicationBuilder builder)
    {
        builder.Services.AddLocalization();
        builder.Services.AddTransient<MudLocalizer, ResourceMudLocalizer>();
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            CultureInfo[] cultures = [.. AppCultures.All.Select(x => x.Culture)];
            options.DefaultRequestCulture = new RequestCulture(AppCultures.Default.Culture);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            // "uk-UA" in Accept-Language picks "uk".
            options.FallBackToParentCultures = true;
            options.FallBackToParentUICultures = true;
        });
    }

    /// <summary>
    /// Saves the chosen language in a cookie and goes back to the page, which reloads in that language.
    /// </summary>
    public static void MapCultureEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("culture/set", (HttpContext context, string culture, string? redirectUri) =>
        {
            if (AppCultures.Find(culture) is { } appCulture)
            {
                context.Response.Cookies.Append(
                    CultureCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(appCulture.Culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax
                    });
            }

            return Results.LocalRedirect(IsLocalPath(redirectUri) ? redirectUri! : "/");
        });
    }

    // "//host" and "/\host" are treated by browsers as another site.
    private static bool IsLocalPath(string? path)
        => !string.IsNullOrEmpty(path) && path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/\\");
}
