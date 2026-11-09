using Microsoft.Extensions.Logging;
#if GTK
using Platform.Maui.Linux.Gtk4.Hosting;
#endif

namespace HomeManagementClient
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
#if GTK
                .UseMauiAppLinuxGtk4<App>()
#else
                .UseMauiApp<App>()
#endif
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
