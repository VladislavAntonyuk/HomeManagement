using HomeManagement.DeviceSdk;

namespace HomeManagement.Cameras;

public class CamerasOptions
{
    /// <summary>
    /// Password cameras use to publish their RTMP stream, e.g. rtmp://host:1935/live/stream?password=...
    /// </summary>
    public string StreamKey { get; set; } = string.Empty;

    /// <summary>
    /// Port of the RTMP server cameras publish to.
    /// </summary>
    public int RtmpPort { get; set; } = 1935;

    /// <summary>
    /// Address of the Frigate API this module reads the camera configuration from.
    /// </summary>
    public string FrigateApiUrl { get; set; } = "https+http://frigate-service";

    /// <summary>
    /// Address people open Frigate at, used for snapshot links in detections.
    /// </summary>
    public string FrigatePublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// Longest on-demand recording (Start Clip). A clip ends by itself after this time,
    /// e.g. when Stop Clip never comes because the hub restarted.
    /// </summary>
    public int ClipMaxSeconds { get; set; } = 3600;

    /// <summary>
    /// MQTT client id the module uses for the Frigate topics.
    /// </summary>
    public string FrigateClientId { get; set; } = "home-management-frigate";

    /// <summary>
    /// The device Frigate is exposed as. Its id is also its MQTT client id, so it must differ from Frigate's own client id, which is <c>frigate</c> by default.
    /// </summary>
    public DeviceAgentOptions Device { get; set; } = new() { DeviceName = "Frigate", DeviceId = "frigate-module" };
}
