using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Router;
using HomeManagement.Components.Dialogs;
using HomeManagement.DeviceSdk;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using System.Diagnostics;

namespace HomeManagement.Components.Pages;

public partial class Home(
     ISnackbar snackbar,
     IRouterController routerController,
     DeviceService deviceService,
     IDialogService dialogService) : ComponentBase
{

    private int _totalDevices;
    private string? _localIp;
    private string? _uptime;
    private string? _temperature;
    private string? _diskSpace;
    private string? _networkData;
    private string? _cpuInfo;
    private string? _ramInfo;

    protected override async Task OnInitializedAsync()
    {
        _localIp = HostInfo.LocalIpAddress;
        _totalDevices = (await deviceService.GetStatusSummaryAsync()).Total;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var uptime = GetUptime();
        var temperature = GetTemperature();
        var diskSpace = GetDiskSpace();
        var networkData = GetNetworkData();
        var cpuInfo = GetCpuInfo();
        var ramInfo = GetRamInfo();
        await Task.WhenAll(uptime, temperature, diskSpace, networkData, cpuInfo, ramInfo);

        _uptime = uptime.Result;
        _temperature = temperature.Result;
        _diskSpace = diskSpace.Result;
        _networkData = networkData.Result;
        _cpuInfo = cpuInfo.Result;
        _ramInfo = ramInfo.Result;
        StateHasChanged();
    }

    private static async Task<string> GetUptime()
    {
        var (_, output) = await RunCommandAsync("uptime", "-p");
        return output;
    }

    private static async Task<string> GetTemperature()
    {
        var (isSuccess, output) = await RunCommandAsync(
            "/bin/bash",
            "-lc",
            "paste <(cat /sys/class/thermal/thermal_zone*/type) <(cat /sys/class/thermal/thermal_zone*/temp | awk '{print $1/1000 \"°C\"}')");

        if (!isSuccess)
        {
            return output;
        }

        var lines = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .Select(parts => $"{parts[0]}: {parts[1]}")
            .ToArray();

        return lines.Length == 0 ? output : string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> GetDiskSpace()
    {
        var (isSuccess, output) = await RunCommandAsync("df", "-T", "-h", "-x", "tmpfs", "-x", "devtmpfs");
        if (!isSuccess)
        {
            return output;
        }

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length <= 1)
        {
            return output;
        }

        var formatted = lines.Skip(1)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 7)
            .Select(parts => $"{string.Join(' ', parts.Skip(6))}: {parts[3]}/{parts[2]} ({parts[5]}) [{parts[1]}]")
            .ToArray();

        return formatted.Length == 0 ? output : string.Join(Environment.NewLine, formatted);
    }

    private static async Task<string> GetNetworkData()
    {
        var (isSuccess, output) = await RunCommandAsync("ip", "-brief", "address", "show");
        if (!isSuccess)
        {
            return output;
        }

        var formatted = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => $"{parts[0]}: {parts[1]} - {(parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : "No address")}")
            .ToArray();

        return formatted.Length == 0 ? output : string.Join(Environment.NewLine, formatted);
    }

    private static async Task<string> GetRamInfo()
    {
        var (isSuccess, output) = await RunCommandAsync("free", "-h");
        if (!isSuccess)
        {
            return output;
        }

        var lines = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length <= 1)
        {
            return output;
        }

        var headers = lines[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (headers.Length == 0)
        {
            return output;
        }

        var formatted = lines.Skip(1)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts =>
            {
                var label = parts[0].TrimEnd(':');
                var values = parts.Skip(1).ToArray();
                var metrics = headers
                    .Zip(values, (header, value) => $"{header}: {value}")
                    .ToArray();

                return metrics.Length == 0
                    ? string.Join(' ', parts)
                    : $"{label}: {string.Join(", ", metrics)}";
            })
            .ToArray();

        return formatted.Length == 0 ? output : string.Join(Environment.NewLine, formatted);
    }

    private static async Task<string> GetCpuInfo()
    {
        var (isSuccess, output) = await RunCommandAsync("lscpu");
        if (!isSuccess)
        {
            return output;
        }

        var details = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(parts => parts[1])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var cpuInfo = new[]
        {
            BuildInfoLine(details, "Model name", "Model", includeAllValues: true),
            BuildInfoLine(details, "Architecture", "Architecture"),
            BuildInfoLine(details, "CPU(s)", "Logical CPUs"),
            BuildInfoLine(details, "Core(s) per socket", "Cores/socket"),
            BuildInfoLine(details, "Thread(s) per core", "Threads/core"),
        }
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToArray();

        return cpuInfo.Length == 0 ? output : string.Join(Environment.NewLine, cpuInfo);
    }

    private static string? BuildInfoLine(IReadOnlyDictionary<string, string[]> details, string key, string label, bool includeAllValues = false)
    {
        if (!details.TryGetValue(key, out var values) || values.Length == 0)
        {
            return null;
        }

        var value = includeAllValues ? string.Join(", ", values) : values[0];
        return $"{label}: {value}";
    }

    // A command that never ends, e.g. "top", must not block the page.
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private static async Task<(bool IsSuccess, string Output)> RunCommandAsync(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var timeout = new CancellationTokenSource(CommandTimeout);
            var result = await Process.RunAndCaptureTextAsync(startInfo, timeout.Token);
            var output = result.ExitStatus.ExitCode == 0 ? result.StandardOutput : result.StandardError;
            return (result.ExitStatus.ExitCode == 0, output.Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, $"The command did not finish within {CommandTimeout.TotalSeconds} seconds.");
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private async Task Reboot()
    {
        if (!await dialogService.ConfirmAsync("Reboot server", "Reboot the Home Management server?", "Reboot"))
        {
            return;
        }

        Process.StartAndForget("reboot");
        snackbar.Add("Rebooting...", Severity.Success);
    }

    private async Task Shutdown()
    {
        if (!await dialogService.ConfirmAsync("Shut down server", "Shut down the Home Management server? It has to be powered on manually.", "Shut down"))
        {
            return;
        }

        Process.StartAndForget("poweroff");
        snackbar.Add("Shutting down...", Severity.Success);
    }

    private string _terminalCommand = string.Empty;
    private string _terminalOutput = string.Empty;

    private async Task RunTerminalCommand()
    {
        if (string.IsNullOrWhiteSpace(_terminalCommand))
        {
            return;
        }

        var (_, output) = await RunCommandAsync("/bin/bash", "-lc", _terminalCommand);
        _terminalOutput = $"> {_terminalCommand}{Environment.NewLine}{output}";
    }

    private async Task OnTerminalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await RunTerminalCommand();
        }
    }

    private async Task RebootRouter()
    {
        if (!await dialogService.ConfirmAsync("Reboot router", "Reboot the router? The network will be unavailable for a few minutes.", "Reboot"))
        {
            return;
        }

        var result = await routerController.RebootAsync();
        snackbar.Add(result.IsSuccess ? "Router rebooted successfully." : $"Error rebooting router: {result.Message}", result.IsSuccess ? Severity.Success : Severity.Error);
    }

    private async Task RebootRouterModem()
    {
        if (!await dialogService.ConfirmAsync("Reboot modem", "Reboot the modem? The internet will be unavailable for a few minutes.", "Reboot"))
        {
            return;
        }

        var result = await routerController.RebootModemAsync();
        snackbar.Add(result.IsSuccess ? "Router Modem rebooted successfully." : $"Error rebooting router modem: {result.Message}", result.IsSuccess ? Severity.Success : Severity.Error);
    }

    private async Task TurnOffRouterLeds()
    {
        var result = await routerController.SetLedsAsync(false);
        snackbar.Add(result.IsSuccess ? "Router LEDs turned off successfully." : $"Error turning off router LEDs: {result.Message}", result.IsSuccess ? Severity.Success : Severity.Error);
    }
}