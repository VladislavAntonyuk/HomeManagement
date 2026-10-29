using HomeManagement.Application.DeviceManagement;

namespace HomeManagement.Infrastructure.Extensions;

public static class DeviceExtensions
{
    public static void AddDevices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DeviceStateStore>();
        builder.Services.AddSingleton<DeviceRegistry>();
        builder.Services.AddSingleton<DeviceHub>();
        builder.Services.AddSingleton<IDeviceHub>(sp => sp.GetRequiredService<DeviceHub>());
        builder.Services.AddSingleton<IDeviceActionExecutor, DeviceActionExecutor>();
    }
}
