using System.Collections.Frozen;
using System.Diagnostics;
using HomeManagement.DeviceSdk;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;

namespace HomeManagement.Devices.ProcessDevice;

/// <summary>
/// A device whose actions run processes configured in <see cref="CommandsSettings"/>, plus shutdown and restart of the machine.
/// The commands come from configuration and never change while the device runs.
/// </summary>
public sealed class ProcessDeviceHandler(IOptions<CommandsSettings> commandsOptions) : IDeviceHandler
{
    private const string ShutdownKey = "shutdown";
    private const string RestartKey = "restart";

    private readonly FrozenDictionary<string, CommandInfo> _commands =
        commandsOptions.Value.Commands.ToFrozenDictionary(x => x.Endpoint, StringComparer.Ordinal);

    private readonly IReadOnlyList<DeviceAction> _actions =
    [
        new("SHUTDOWN", CommandType.Post, ShutdownKey),
        new("RESTART", CommandType.Post, RestartKey),
        .. commandsOptions.Value.Commands.Select(x => new DeviceAction(x.Name, x.Method, x.Endpoint))
    ];

    public IReadOnlyList<DeviceAction> GetActions() => _actions;

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

        // DeviceAgent only sends commands of Post actions, so the command is configured.
        return await CommandRunner.RunAsync(_commands[command], token);
    }

    public async Task<string?> ReadAsync(string key, CancellationToken token)
    {
        if (!_commands.TryGetValue(key, out var info))
        {
            return null;
        }

        var result = await CommandRunner.RunAsync(info, token);
        return result.IsSuccess ? result.Output : throw new InvalidOperationException(result.Output);
    }
}
