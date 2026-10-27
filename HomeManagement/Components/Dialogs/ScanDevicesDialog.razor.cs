using HomeManagement.Application.DeviceManagement;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class ScanDevicesDialog(IDeviceDiscoveryService deviceDiscoveryService) : ComponentBase
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(100);

    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;

    [Parameter] public IReadOnlyCollection<string> ExistingDevices { get; set; } = [];

    private List<SelectableDevice> _devices = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        var baseIp = Shared.NetworkManager.GetLocalSubnet();
        var networkDevices = await deviceDiscoveryService.ScanNetworkAsync(baseIp, PingTimeout, CancellationToken.None);
        var existing = new HashSet<string>(ExistingDevices, StringComparer.OrdinalIgnoreCase);

        _devices = networkDevices
            .Select(d => new SelectableDevice
            {
                Device = d,
                IsExisting = existing.Contains(d.Name) || existing.Contains(d.Address)
            })
            .ToList();
        _loading = false;
    }

    private void Save()
    {
        var chosen = _devices.Where(d => d.Selected).Select(d => d.Device).ToList();
        DialogReference.Close(DialogResult.Ok(chosen));
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());
}
