using System.Net;

namespace IpCameraMaui;

public interface ILocalIpService
{
	IPAddress GetLocalIpAddress();
}