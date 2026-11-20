using System.Diagnostics;
using HomeManagement.Shared;

namespace HomeManagement.Devices.ProcessDevice;

public static class CommandRunner
{
    /// <summary>
    /// Runs the command. Every item of <see cref="CommandInfo.Arguments"/> is passed as one argument, so it may contain spaces.
    /// </summary>
    /// <returns>The output without the trailing newline, so a workflow can compare it, e.g. "1".</returns>
    public static async Task<CommandResponse> RunAsync(CommandInfo command, CancellationToken token)
    {
        var result = await Process.RunAndCaptureTextAsync(new ProcessStartInfo(command.FileName, command.Arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }, token);

        if (result.ExitStatus.ExitCode == 0)
        {
            return new CommandResponse(true, result.StandardOutput.TrimEnd());
        }

        // Many scripts report errors on standard output, so fall back to it.
        var error = result.StandardError.TrimEnd() is { Length: > 0 } stderr ? stderr : result.StandardOutput.TrimEnd();
        return new CommandResponse(false, $"Exit code {result.ExitStatus.ExitCode}: {error}");
    }
}
