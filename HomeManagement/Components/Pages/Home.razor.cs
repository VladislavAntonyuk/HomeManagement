using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Host;
using HomeManagement.Application.Router;
using HomeManagement.Components.Dialogs;
using HomeManagement.DeviceSdk;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HomeManagement.Components.Pages;

public partial class Home(
     ISnackbar snackbar,
     IRouterController routerController,
     DeviceService deviceService,
     IDialogService dialogService) : ComponentBase, IDisposable
{

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _disposed = new();
    private readonly string _hostName = Environment.MachineName;
    private readonly string _osDescription = $"{RuntimeInformation.OSDescription} · .NET {Environment.Version}";
    private readonly DateTime _bootedAt = DateTime.Now - HostInfo.Uptime;
    private DeviceStatusSummary? _devices;
    private string? _localIp;
    private string? _uptime;
    private IReadOnlyList<HostTemperature> _temperatures = [];
    private HostTemperature? _hottest;
    private DateTime? _updatedAt;
    private IReadOnlyList<DiskUsage>? _disks;
    private IReadOnlyList<NetworkLink>? _network;
    private CpuModel? _cpuModel;
    private CpuSample? _cpuSample;
    private double? _cpuUsage;
    private IReadOnlyList<double> _loadAverage = [];
    private MemoryUsage? _memory;
    private bool _metricsLoaded;

    protected override async Task OnInitializedAsync()
    {
        _localIp = HostInfo.LocalIpAddress;
        RefreshSensors();
        _devices = await deviceService.GetStatusSummaryAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // The first CPU usage needs two samples, so the first refresh comes after a short wait instead of the full interval.
        _cpuSample = HostMetrics.SampleCpu();
        _cpuModel = await HostMetrics.CpuModelAsync();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), _disposed.Token);
            using var timer = new PeriodicTimer(RefreshInterval);
            do
            {
                RefreshHost();
                _devices = await deviceService.GetStatusSummaryAsync(_disposed.Token);
                _updatedAt = DateTime.Now;
                await InvokeAsync(StateHasChanged);
            }
            while (await timer.WaitForNextTickAsync(_disposed.Token));
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
    }

    // Only cancelled, not disposed: the refresh loop may still read the token after the page is gone.
    public void Dispose() => _disposed.Cancel();

    private void RefreshHost()
    {
        RefreshSensors();
        var sample = HostMetrics.SampleCpu();
        _cpuUsage = HostMetrics.CpuUsagePercent(_cpuSample, sample) ?? _cpuUsage;
        _cpuSample = sample;
        _loadAverage = HostMetrics.LoadAverage();
        _memory = HostMetrics.Memory();
        _disks = HostMetrics.Disks();
        _network = HostMetrics.Network();
        _metricsLoaded = true;
    }

    private void RefreshSensors()
    {
        _uptime = DisplayFormatter.FormatDuration(HostInfo.Uptime);
        _temperatures = HostInfo.Temperatures;
        _hottest = _temperatures.MaxBy(x => x.Celsius);
    }

    private NetworkLink? LocalLink() => _localIp is null
        ? null
        : _network?.FirstOrDefault(x => x.IsUp && x.Addresses.Contains(_localIp));

    // "bigcore0-thermal" reads as "Bigcore0".
    private static string SensorName(string sensor)
    {
        var name = sensor.Replace("-thermal", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_thermal", string.Empty, StringComparison.OrdinalIgnoreCase);
        return name.Length == 0 ? sensor : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static Color TemperatureColor(double? celsius) => celsius switch
    {
        null => Color.Info,
        >= 80 => Color.Error,
        >= 65 => Color.Warning,
        _ => Color.Success
    };

    private static string FormatSize(long bytes) => DisplayFormatter.FormatBytes(bytes);

    private static string FormatCpuModel(CpuModel model) => model.Clusters.Count == 0
        ? string.Format(Strings.Home_Cores, model.LogicalCpus)
        : string.Join(" + ", model.Clusters.Select(x => x.Cores is { } cores ? $"{cores}× {x.Name}" : x.Name));

    private static string NetworkIcon(NetworkKind kind) => kind switch
    {
        NetworkKind.Ethernet => Icons.Material.Filled.SettingsEthernet,
        NetworkKind.WiFi => Icons.Material.Filled.Wifi,
        NetworkKind.Vpn => Icons.Material.Filled.VpnLock,
        _ => Icons.Material.Filled.Lan
    };

    private static string NetworkKindName(NetworkKind kind) => kind switch
    {
        NetworkKind.Ethernet => "Ethernet",
        NetworkKind.WiFi => "Wi-Fi",
        NetworkKind.Vpn => "VPN",
        _ => Strings.Home_Network
    };

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
            return (false, string.Format(Strings.Home_CommandTimeout, CommandTimeout.TotalSeconds));
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private async Task Reboot()
    {
        if (!await dialogService.ConfirmAsync(Strings.Home_RebootServerTitle, Strings.Home_RebootServerMessage, Strings.Home_Reboot))
        {
            return;
        }

        Process.StartAndForget("reboot");
        snackbar.Add(Strings.Home_Rebooting, Severity.Success);
    }

    private async Task Shutdown()
    {
        if (!await dialogService.ConfirmAsync(Strings.Home_ShutDownTitle, Strings.Home_ShutDownMessage, Strings.Home_ShutDown))
        {
            return;
        }

        Process.StartAndForget("poweroff");
        snackbar.Add(Strings.Home_ShuttingDown, Severity.Success);
    }

    private string _terminalCommand = string.Empty;
    private string _terminalOutput = string.Empty;
    private bool _terminalRunning;

    private async Task RunTerminalCommand()
    {
        if (_terminalRunning || string.IsNullOrWhiteSpace(_terminalCommand))
        {
            return;
        }

        var command = _terminalCommand;
        _terminalRunning = true;
        _terminalOutput = $"$ {command}{Environment.NewLine}{Strings.Common_Running}";
        try
        {
            var (_, output) = await RunCommandAsync("/bin/bash", "-lc", command);
            _terminalOutput = $"$ {command}{Environment.NewLine}{output}";
            _terminalCommand = string.Empty;
        }
        finally
        {
            _terminalRunning = false;
        }
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
        if (!await dialogService.ConfirmAsync(Strings.Home_RebootRouter, Strings.Home_RebootRouterMessage, Strings.Home_Reboot))
        {
            return;
        }

        var result = await routerController.RebootAsync();
        snackbar.Add(result.IsSuccess ? Strings.Home_RouterRebooted : string.Format(Strings.Home_RouterRebootFailed, result.Message), result.IsSuccess ? Severity.Success : Severity.Error);
    }

    private async Task RebootRouterModem()
    {
        if (!await dialogService.ConfirmAsync(Strings.Home_RebootModem, Strings.Home_RebootModemMessage, Strings.Home_Reboot))
        {
            return;
        }

        var result = await routerController.RebootModemAsync();
        snackbar.Add(result.IsSuccess ? Strings.Home_ModemRebooted : string.Format(Strings.Home_ModemRebootFailed, result.Message), result.IsSuccess ? Severity.Success : Severity.Error);
    }

    private async Task TurnOffRouterLeds()
    {
        var result = await routerController.SetLedsAsync(false);
        snackbar.Add(result.IsSuccess ? Strings.Home_LedsOff : string.Format(Strings.Home_LedsOffFailed, result.Message), result.IsSuccess ? Severity.Success : Severity.Error);
    }
}