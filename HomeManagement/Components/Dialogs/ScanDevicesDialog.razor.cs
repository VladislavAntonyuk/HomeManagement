using HomeManagement.Application.DeviceManagement;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class ScanDevicesDialog(ISnackbar snackbar) : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;
    private List<SelectableDevice> _devices = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var baseIp = Shared.NetworkManager.GetLocalSubnet();

            var networkDevices = await NetworkManager.ScanNetworkAsync(baseIp, 100, CancellationToken.None);

            _devices = networkDevices.Select(d => new SelectableDevice { Device = d }).ToList();
        }
        catch (Exception e)
        {
            snackbar.Add(e.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save()
    {
        var chosen = _devices.Where(d => d.Selected).Select(d => d.Device).ToList();
        DialogReference.Close(DialogResult.Ok(chosen));
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());

    public static string FormatDuration(long totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);

        var parts = new List<string>();

        if (ts.Days > 0)
        {
            parts.Add($"{ts.Days} day{(ts.Days > 1 ? "s" : "")}");
        }

        if (ts.Hours > 0 || parts.Count > 0)
        {
            parts.Add($"{ts.Hours} hour{(ts.Hours != 1 ? "s" : "")}");
        }

        if (ts.Minutes > 0 || parts.Count > 0)
        {
            parts.Add($"{ts.Minutes} minute{(ts.Minutes != 1 ? "s" : "")}");
        }

        parts.Add($"{ts.Seconds} second{(ts.Seconds != 1 ? "s" : "")}");

        return string.Join(", ", parts);
    }
}