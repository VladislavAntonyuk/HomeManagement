using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Maui.Alerts;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;

namespace BluetoothController.Client;

public partial class MainPage : ContentPage
{
    public ObservableCollection<IDevice> Devices { get; set; } = new();

    public ObservableCollection<string> Commands { get; set; } = new()
    {
        "GPIO",
        "REBOOT",
        "SHUTDOWN"
    };

    public MainPage()
    {
        InitializeComponent();
        BindingContext = this;

        Password.Text = Preferences.Get("Password", string.Empty);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        CommandsPicker.SelectedIndex = 0;
        await Permissions.RequestAsync<Permissions.Bluetooth>();
        CrossBluetoothLE.Current.StateChanged += async (s, e) =>
        {
            await Toast.Make($"The bluetooth state changed to {e.NewState}").Show();
        };
        CrossBluetoothLE.Current.Adapter.DeviceDiscovered += async (s, a) =>
        {
            if (!Devices.Contains(a.Device))
            {
                Devices.Add(a.Device);
            }
        };
    }

    private async void OnCounterClicked(object? sender, EventArgs e)
    {
        await CrossBluetoothLE.Current.Adapter.StartScanningForDevicesAsync();
    }

    private async void SelectableItemsView_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var device = e.CurrentSelection.OfType<IDevice>().FirstOrDefault();
        if (device == null)
        {
            return;
        }

        try
        {
            await CrossBluetoothLE.Current.Adapter.ConnectToDeviceAsync(device, new ConnectParameters(true, true, ConnectionParameterSet.Balanced));
            var services = await device.GetServicesAsync();
            var service = await device.GetServiceAsync(Guid.Parse("12345678-1234-5678-1234-56789abcdef0"));
            var characteristic =
                await service.GetCharacteristicAsync(Guid.Parse("12345678-1234-5678-1234-56789abcdef1"));
            await characteristic.WriteAsync(
                Encoding.Default.GetBytes($"{Password};{CommandsPicker.SelectedItem};10;OUTPUT;1;"));
        }
        catch (Exception ex)
        {
            await Toast.Make(ex.Message).Show();
        }
    }

    private void Password_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        Preferences.Set("Password", e.NewTextValue);
    }
}