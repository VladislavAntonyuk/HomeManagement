namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Frigate MQTT topics, see https://docs.frigate.video/integrations/mqtt.
/// </summary>
public static class FrigateTopics
{
    public const string On = "ON";
    public const string Off = "OFF";

    /// <summary>
    /// Frigate retains <see cref="Online"/> here and sets <c>offline</c> as its last will.
    /// </summary>
    public const string Available = "frigate/available";
    public const string Online = "online";

    public const string Events = "frigate/events";

    public const string ProfileSet = "frigate/profile/set";

    /// <summary>
    /// Retained name of the active profile, or <see cref="NoProfile"/>.
    /// </summary>
    public const string ProfileState = "frigate/profile/state";
    public const string NoProfile = "none";

    public const string AllRecordingStates = "frigate/+/recordings/state";
    public const string AllEnabledStates = "frigate/+/enabled/state";

    public static string RecordingsSet(string camera) => $"frigate/{camera}/recordings/set";

    public static string EnabledSet(string camera) => $"frigate/{camera}/enabled/set";

    /// <summary>
    /// Parses "frigate/{camera}/{feature}/state".
    /// </summary>
    public static bool TryParseCameraState(string topic, out string camera, out string feature)
    {
        var parts = topic.Split('/');
        if (parts is ["frigate", var cameraPart, var featurePart, "state"])
        {
            camera = cameraPart;
            feature = featurePart;
            return true;
        }

        camera = string.Empty;
        feature = string.Empty;
        return false;
    }
}
