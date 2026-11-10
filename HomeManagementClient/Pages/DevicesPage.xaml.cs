namespace HomeManagementClient.Pages;

public partial class DevicesPage : ContentPage
{
    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
