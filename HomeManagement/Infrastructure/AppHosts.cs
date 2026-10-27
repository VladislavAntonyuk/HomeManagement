namespace HomeManagement.Infrastructure;

public static class AppHosts
{
    public const string MainHost = "home-management.local";

    public const string CamerasHost = $"cameras.{MainHost}";

    public static bool IsMainHostOrSubdomain(string host)
        => host.Equals(MainHost, StringComparison.OrdinalIgnoreCase)
           || host.EndsWith($".{MainHost}", StringComparison.OrdinalIgnoreCase);
}
