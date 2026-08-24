using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Infrastructure;

[McpServerToolType]
public class HomeManagementMcpTools
{
    [McpServerTool, Description("Lists all available devices and actions")]
    public static async Task<List<Device>> ListDevices(HomeManagementDbContext dbContext, CancellationToken token)
    {
        var devices = await dbContext.Devices.AsNoTracking().Include(x => x.Actions).ToListAsync(token);
        return devices;
    }

    [McpServerTool, Description("Searches for an action by name and a device by device name, Then executes the action")]
    public static async Task<DeviceActionExecutionResult> RunAction(string deviceName, string action, IDeviceActionExecutor deviceActionExecutor, HomeManagementDbContext dbContext, CancellationToken token)
    {
        var device = await dbContext.Devices.AsNoTracking().Include(x => x.Actions).FirstOrDefaultAsync(x => x.Name == deviceName, token);
        if (device is null)
        {
            return new DeviceActionExecutionResult(false, "Device not found");
        }

        var deviceAction = device.Actions.FirstOrDefault(x => x.Action == action);
        if (deviceAction is null)
        {
            return new DeviceActionExecutionResult(false, "Action not found");
        }

        var result = await deviceActionExecutor.ExecuteAsync(device, deviceAction, token);
        return result;
    }
}