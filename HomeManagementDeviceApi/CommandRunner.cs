using System.Diagnostics;
using HomeManagement.Shared;

namespace HomeManagementDeviceApi;

public static class CommandRunner
{
    public static async Task<CommandResponse> RunAsync(CommandInfo command, CancellationToken token)
    {
        var result = await Process.RunAndCaptureTextAsync(new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = string.Join(' ', command.Arguments),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }, token);

        return result.ExitStatus.ExitCode == 0
            ? new CommandResponse(true, result.StandardOutput)
            : new CommandResponse(false, result.StandardError);
    }
}
