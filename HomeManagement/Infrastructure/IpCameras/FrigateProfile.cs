namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateProfile
{
    public const string SetTopic = "frigate/profile/set";
    public const string Away = "away";
    public const string Default = "default";

    private FrigateProfile()
    {
    }
}
