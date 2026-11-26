using System.Globalization;

namespace HomeManagement.Infrastructure.Localization;

/// <summary>
/// A language the UI is translated to.
/// </summary>
/// <param name="NativeName">The name of the language in itself, so people find their language whatever the current one is.</param>
public sealed record AppCulture(CultureInfo Culture, string NativeName);

public static class AppCultures
{
    public static readonly AppCulture Default = new(CultureInfo.GetCultureInfo("en"), "English");

    public static readonly IReadOnlyList<AppCulture> All =
    [
        Default,
        new(CultureInfo.GetCultureInfo("uk"), "Українська")
    ];

    public static AppCulture? Find(string? name)
        => All.FirstOrDefault(x => string.Equals(x.Culture.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The language of the current request, or its parent language, e.g. "uk" for "uk-UA".
    /// </summary>
    public static AppCulture Current
    {
        get
        {
            for (var culture = CultureInfo.CurrentUICulture; !Equals(culture, CultureInfo.InvariantCulture); culture = culture.Parent)
            {
                if (Find(culture.Name) is { } found)
                {
                    return found;
                }
            }

            return Default;
        }
    }
}
