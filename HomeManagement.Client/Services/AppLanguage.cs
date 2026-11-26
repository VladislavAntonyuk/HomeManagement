using System.Globalization;

namespace HomeManagement.Client.Services;

/// <summary>
/// The app language the user picked in Settings, kept in preferences.
/// Supported languages are English and Ukrainian; any other system language falls back to English.
/// </summary>
public sealed class AppLanguage(IPreferences preferences)
{
    private const string LanguageKey = "AppLanguage";

    public const string SystemDefault = "";
    public const string English = "en";
    public const string Ukrainian = "uk";

    /// <summary>
    /// The system culture, captured before the app overrides the current culture.
    /// </summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    public event EventHandler? Changed;

    /// <summary>
    /// The picked language code, or an empty string for the system language.
    /// </summary>
    public string Code => Normalize(preferences.Get(LanguageKey, SystemDefault));

    /// <summary>
    /// Sets the current culture to the picked language. Call it before any UI is created.
    /// </summary>
    public void Apply()
    {
        var culture = Resolve(Code);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Set(string code)
    {
        code = Normalize(code);
        if (code == Code)
        {
            return;
        }

        preferences.Set(LanguageKey, code);
        Apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string? code) => code is English or Ukrainian ? code : SystemDefault;

    /// <summary>
    /// Keeps the regional formats of the system when it already uses the language.
    /// </summary>
    private static CultureInfo Resolve(string code)
    {
        var language = code.Length > 0 ? code : SystemCulture.TwoLetterISOLanguageName;
        if (language is not Ukrainian)
        {
            language = English;
        }

        if (SystemCulture.TwoLetterISOLanguageName == language)
        {
            return SystemCulture;
        }

        return CultureInfo.GetCultureInfo(language == Ukrainian ? "uk-UA" : "en-US");
    }
}
