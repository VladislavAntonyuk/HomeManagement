using Com.Pedro.Rtmp.Utils;

namespace IpCameraMaui;

public class ConnectChecker(Action<string> action) : Java.Lang.Object, IConnectCheckerRtmp
{
    /// <inheritdoc />
    public void OnAuthErrorRtmp()
    {
        action("Authentication error");
    }

    /// <inheritdoc />
    public void OnAuthSuccessRtmp()
    {
        action("Authentication success");
    }

    /// <inheritdoc />
    public void OnConnectionFailedRtmp(string reason)
    {
        action($"Connection failed: {reason}");
    }

    /// <inheritdoc />
    public void OnConnectionStartedRtmp(string rtmpUrl)
    {
        action($"Connection started: {rtmpUrl}");
    }

    /// <inheritdoc />
    public void OnConnectionSuccessRtmp()
    {
        action("Connection success");
    }

    /// <inheritdoc />
    public void OnDisconnectRtmp()
    {
        action("Disconnected");
    }

    /// <inheritdoc />
    public void OnNewBitrateRtmp(long bitrate)
    {
        action($"New bitrate: {bitrate} bps");
    }
}