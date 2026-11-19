using CommunityToolkit.Maui;
using RtmpCameraViewModel = IpCameraMaui.ViewModels.RtmpCameraViewModel;
using SettingsViewModel = IpCameraMaui.ViewModels.SettingsViewModel;

namespace IpCameraMaui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureMauiHandlers(x =>
			{
#if ANDROID
				x.AddHandler<MauiSurfaceView, RtpLibrary.MauiSurfaceViewHandler>();
#endif
			}); ;

		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<RtmpCameraViewModel>();

		builder.Services.AddSingleton<ILocalIpService, LocalIpService>();
		builder.Services.AddSingleton<IAutoStartService, AutoStartService>();
		builder.Services.AddSingleton(Preferences.Default);

		return builder.Build();
	}
}