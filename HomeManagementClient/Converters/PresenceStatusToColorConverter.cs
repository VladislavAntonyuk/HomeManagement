using System.Globalization;
using CommunityToolkit.Maui.Converters;
using HomeManagement.Shared.Api;

namespace HomeManagementClient.Converters;

/// <summary>
/// Colors a device presence: green online, red offline, amber not responding, gray unknown.
/// </summary>
public sealed class PresenceStatusToColorConverter : BaseConverterOneWay<DevicePresenceStatus, Color>
{
    public override Color DefaultConvertReturnValue { get; set; } = Colors.Gray;

    public override Color ConvertFrom(DevicePresenceStatus value, CultureInfo? culture) => value switch
    {
        DevicePresenceStatus.Online => GetColor("StatusOnline"),
        DevicePresenceStatus.Offline => GetColor("StatusOffline"),
        DevicePresenceStatus.NotResponding => GetColor("StatusWarning"),
        _ => GetColor("StatusUnknown")
    };

    private Color GetColor(string key)
        => Application.Current?.Resources.TryGetValue(key, out var color) == true && color is Color value ? value : DefaultConvertReturnValue;
}
