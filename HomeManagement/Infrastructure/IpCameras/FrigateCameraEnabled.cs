namespace HomeManagement.Infrastructure.IpCameras;

public sealed class FrigateCameraEnabled
{
    public const string On = "ON";
    public const string Off = "OFF";

    private FrigateCameraEnabled()
    {
    }

    public static string SetTopic(string camera) => $"frigate/{camera}/enabled/set";
}
