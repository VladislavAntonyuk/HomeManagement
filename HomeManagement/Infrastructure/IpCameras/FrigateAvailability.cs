namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateAvailability
{
    /// <summary>
    /// Frigate retains <see cref="Online"/> here and sets <c>offline</c> as its last will.
    /// </summary>
    public const string Topic = "frigate/available";
    public const string Online = "online";

    private FrigateAvailability()
    {
    }
}
