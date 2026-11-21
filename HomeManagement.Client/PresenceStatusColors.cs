using HomeManagement.Shared.Api;

namespace HomeManagement.Client;

public static class PresenceStatusColors
{
    extension(DevicePresenceStatus status)
    {
        /// <summary>
        /// The status color of the app theme, used in XAML as {Status.Color}.
        /// </summary>
        public Color Color => GetColor(status switch
        {
            DevicePresenceStatus.Online => "StatusOnline",
            DevicePresenceStatus.Offline => "StatusOffline",
            DevicePresenceStatus.NotResponding => "StatusWarning",
            _ => "StatusUnknown"
        });
    }

    private static Color GetColor(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Gray;
}
