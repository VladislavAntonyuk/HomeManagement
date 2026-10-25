using HomeManagement.Application.AwayMode;
using HomeManagement.Application.Router;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Diagnostics;

namespace HomeManagement.Components.Pages;

public partial class Home(
     ISnackbar snackbar,
     IRouterController routerController,
     IDbContextFactory<HomeManagementDbContext> dbContextFactory,
     AwayModeService awayModeService) : ComponentBase
{
    private int _totalDevices;
    private string? _localIp;
    private string? _uptime;
    private string? _temperature;
    private string? _diskSpace;
    private string? _networkData;
    private string? _cpuInfo;
    private string? _ramInfo;
    private bool _isAwayModeEnabled;
    private bool _isUpdatingAwayMode;

    protected override async Task OnInitializedAsync()
    {
        _localIp = NetworkManager.GetLocalIp();
        _uptime = await GetUptime();
        _temperature = await GetTemperature();
        _diskSpace = await GetDiskSpace();
        _networkData = await GetNetworkData();
        _cpuInfo = await GetCpuInfo();
        _ramInfo = await GetRamInfo();
        await using var db = await dbContextFactory.CreateDbContextAsync();
        _totalDevices = await db.Devices.CountAsync();
        _isAwayModeEnabled = await awayModeService.IsEnabledAsync();
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

    private async Task<string> GetDiskSpace()
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

    private async Task<string> GetNetworkData()
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

    private async Task<string> GetRamInfo()
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

    private async Task<string> GetCpuInfo()
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

    private static async Task<(bool IsSuccess, string Output)> RunCommandAsync(string fileName, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            var result = await Process.RunAndCaptureTextAsync(startInfo);
            var output = result.ExitStatus.ExitCode == 0 ? result.StandardOutput : result.StandardError;
            return (result.ExitStatus.ExitCode == 0, output.Trim());
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private void Reboot()
    {
        try
        {
            Process.StartAndForget("reboot");
            snackbar.Add("Rebooting...", Severity.Success);
        }
        catch (Exception e)
        {
            snackbar.Add(e.Message, Severity.Error);
        }
    }

    private void Shutdown()
    {
        try
        {
            Process.StartAndForget("poweroff");
            snackbar.Add("Shutting down...", Severity.Success);
        }
        catch (Exception e)
        {
            snackbar.Add(e.Message, Severity.Error);
        }
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

    private async Task OnAwayModeChanged(bool isEnabled)
    {
        _isUpdatingAwayMode = true;

        try
        {
            await awayModeService.SetEnabledAsync(isEnabled);
            _isAwayModeEnabled = isEnabled;
            snackbar.Add($"Away mode {(isEnabled ? "enabled" : "disabled")}.", Severity.Success);
        }
        catch (Exception e)
        {
            snackbar.Add($"Error updating away mode: {e.Message}", Severity.Error);
        }
        finally
        {
            _isUpdatingAwayMode = false;
        }
    }

    private async Task RebootRouter()
    {
        var result = await routerController.Reboot();
        if (string.IsNullOrEmpty(result.ErrorStatus))
        {
            snackbar.Add("Router rebooted successfully.", Severity.Success);
        }
        else
        {
            snackbar.Add($"Error rebooting router: {result.ErrorStatus}", Severity.Error);
        }
    }

    private async Task RebootRouterModem()
    {
        var result = await routerController.RebootModem();
        if (string.IsNullOrEmpty(result.ErrorStatus))
        {
            snackbar.Add("Router Modem rebooted successfully.", Severity.Success);
        }
        else
        {
            snackbar.Add($"Error rebooting router modem: {result.ErrorStatus}", Severity.Error);
        }
    }

    private async Task TurnOffRouterLeds()
    {
        var result = await routerController.SetLeds(false);
        if (string.IsNullOrEmpty(result.ErrorStatus))
        {
            snackbar.Add("Router LEDs turned off successfully.", Severity.Success);
        }
        else
        {
            snackbar.Add($"Error turning off router LEDs: {result.ErrorStatus}", Severity.Error);
        }
    }
}