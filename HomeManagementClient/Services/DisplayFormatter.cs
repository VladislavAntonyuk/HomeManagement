using HomeManagement.Shared.Api;

namespace HomeManagementClient.Services;

public static class DisplayFormatter
{
    public static string FormatAgo(DateTimeOffset? value, string never = "never")
    {
        if (value is null)
        {
            return never;
        }

        var elapsed = DateTimeOffset.UtcNow - value.Value;
        return elapsed switch
        {
            { TotalSeconds: < 10 } => "just now",
            { TotalMinutes: < 1 } => $"{(int)elapsed.TotalSeconds} s ago",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} h ago",
            _ => $"{(int)elapsed.TotalDays} d ago"
        };
    }

    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
        {
            return $"{(int)value.TotalSeconds} s";
        }

        var parts = new List<string>(3);
        if (value.Days > 0)
        {
            parts.Add($"{value.Days} d");
        }

        if (value.Hours > 0)
        {
            parts.Add($"{value.Hours} h");
        }

        if (value.Days == 0 && value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} min");
        }

        return string.Join(' ', parts);
    }

    public static string FormatStatus(DevicePresenceStatus status) => status switch
    {
        DevicePresenceStatus.Online => "Online",
        DevicePresenceStatus.Offline => "Offline",
        DevicePresenceStatus.NotResponding => "Not responding",
        _ => "Unknown"
    };
}
