namespace HomeManagement.Client.Pages;

public partial class DevicesPage : ContentPage
{
    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
