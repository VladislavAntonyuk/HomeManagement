using HomeManagement.Application.DeviceManagement;
using HomeManagement.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class AddressDialog(IDeviceDiscoveryService deviceDiscoveryService)
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;
    private string Address { get; set; } = string.Empty;
    private string Name { get; set; } = string.Empty;
    private string? _error;
    private bool _isBusy;

    private bool IsNameRequired => DeviceAddress.IsPhysicalAddress(Address.Trim()) || !string.IsNullOrEmpty(_error);

    private bool CanSave => !string.IsNullOrWhiteSpace(Address) && (!IsNameRequired || !string.IsNullOrWhiteSpace(Name));

    private async Task Save()
    {
        if (!CanSave)
        {
            return;
        }

        var address = Address.Trim();
        if (DeviceAddress.IsPhysicalAddress(address))
        {
            Close(new DeviceConfig { Name = Name.Trim(), Address = address });
            return;
        }

        _isBusy = true;
        _error = null;
        try
        {
            var device = await deviceDiscoveryService.GetDeviceConfigAsync(address, CancellationToken.None);
            if (device is null)
            {
                _error = "Could not reach device at this address.";
                return;
            }

            device.Address = address;
            Close(string.IsNullOrWhiteSpace(Name)
                ? device
                : new DeviceConfig { Name = Name.Trim(), Address = device.Address, Actions = device.Actions });
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());

    private void AddAnyway()
    {
        if (!CanSave)
        {
            return;
        }

        Close(new DeviceConfig { Name = Name.Trim(), Address = Address.Trim() });
    }

    private void Close(DeviceConfig device) => DialogReference.Close(DialogResult.Ok(device));
}
