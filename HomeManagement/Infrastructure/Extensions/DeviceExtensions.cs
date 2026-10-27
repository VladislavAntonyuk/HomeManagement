using HomeManagement.Application.DeviceManagement;

namespace HomeManagement.Infrastructure.Extensions;

public static class DeviceExtensions
{
    public static void AddDevices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IDeviceActionExecutor, DeviceActionExecutor>();
        builder.Services.AddSingleton<IDeviceDiscoveryService, DeviceDiscoveryService>();
    }
}
