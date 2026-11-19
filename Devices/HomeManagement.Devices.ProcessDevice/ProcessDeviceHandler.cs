using System.Diagnostics;
using HomeManagement.DeviceSdk;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;

namespace HomeManagement.Devices.ProcessDevice;

/// <summary>
/// A device whose actions run processes configured in <see cref="CommandsSettings"/>, plus shutdown and restart of the machine.
/// </summary>
public sealed class ProcessDeviceHandler(IOptions<CommandsSettings> commandsOptions) : IDeviceHandler
{
    private const string ShutdownKey = "shutdown";
    private const string RestartKey = "restart";

    // The commands come from configuration and never change while the device runs.
    public event Action? ActionsChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<DeviceAction> GetActions()
    {
        var actions = new List<DeviceAction>
        {
            new("SHUTDOWN", CommandType.Post, ShutdownKey),
            new("RESTART", CommandType.Post, RestartKey)
        };
        actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction(x.Name, x.Method, x.Endpoint)));
        return actions;
    }

    public async Task<CommandResponse> ExecuteAsync(string command, CancellationToken token)
    {
        switch (command)
        {
            case ShutdownKey:
                Process.StartAndForget("poweroff");
                return new CommandResponse(true, "Shutting down");
            case RestartKey:
                Process.StartAndForget("reboot");
                return new CommandResponse(true, "Restarting");
        }

        var info = commandsOptions.Value.Commands.First(x => x.Endpoint == command);
        return await CommandRunner.RunAsync(info, token);
    }

    public async Task<string?> ReadAsync(string key, CancellationToken token)
    {
        if (commandsOptions.Value.Commands.FirstOrDefault(x => x.Endpoint == key) is not { } info)
        {
            return null;
        }

        var result = await CommandRunner.RunAsync(info, token);
        return result.IsSuccess ? result.Output : throw new InvalidOperationException(result.Output);
    }
}
