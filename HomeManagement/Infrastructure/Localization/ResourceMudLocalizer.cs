using System.Globalization;
using HomeManagement.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace HomeManagement.Infrastructure.Localization;

/// <summary>
/// Translates the texts MudBlazor shows itself, e.g. "Required" or the "Close" button of a dialog.
/// They are kept in <see cref="Strings"/> as "Mud_" plus the MudBlazor key; any other key falls back to MudBlazor's English.
/// </summary>
public sealed class ResourceMudLocalizer : MudLocalizer
{
    private const string Prefix = "Mud_";

    public override LocalizedString this[string key]
        => Strings.ResourceManager.GetString(Prefix + key, CultureInfo.CurrentUICulture) is { } value
            ? new LocalizedString(key, value)
            : new LocalizedString(key, key, resourceNotFound: true);

    public override LocalizedString this[string key, params object[] arguments]
        => Strings.ResourceManager.GetString(Prefix + key, CultureInfo.CurrentUICulture) is { } value
            ? new LocalizedString(key, string.Format(CultureInfo.CurrentCulture, value, arguments))
            : new LocalizedString(key, key, resourceNotFound: true);
}
