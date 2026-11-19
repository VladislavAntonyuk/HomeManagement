namespace IpCameraMaui;

public interface IAutoStartService
{
	Task<bool> IsAutoStartEnabledAsync();
	Task<bool> EnableAutoStartAsync();
	Task<bool> DisableAutoStartAsync();
}