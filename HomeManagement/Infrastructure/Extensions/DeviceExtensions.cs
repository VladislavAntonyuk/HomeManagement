using Microsoft.Extensions.DependencyInjection.Extensions;
using HomeManagement.Application.DeviceManagement;

namespace HomeManagement.Infrastructure.Extensions;

public static class DeviceExtensions
{
    public static void AddDevices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DevicePresenceOptions>(builder.Configuration.GetSection("DevicePresence"));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DeviceStateStore>();
        builder.Services.AddSingleton<DeviceRegistry>();
        builder.Services.AddSingleton<DeviceHub>();
        builder.Services.AddSingleton<IDeviceHub>(sp => sp.GetRequiredService<DeviceHub>());
        builder.Services.AddSingleton<IDeviceActionExecutor, DeviceActionExecutor>();
        builder.Services.AddHostedService<DevicePresenceMonitor>();
    }
}
