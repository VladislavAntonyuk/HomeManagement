namespace HomeManagementClient.Pages;

public partial class DevicesPage : ContentPage
{
    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
#if GTK
        var items = StaticItemsView.Replace(ItemsList, "No devices");
        items.SetBinding(StaticItemsView.ItemsSourceProperty, static (DevicesViewModel vm) => vm.Devices);
        items.SetBinding(StaticItemsView.VersionProperty, static (DevicesViewModel vm) => vm.LoadVersion);
#endif
    }
}
