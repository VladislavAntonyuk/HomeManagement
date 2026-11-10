namespace HomeManagementClient.Pages;

public partial class CamerasPage : ContentPage
{
    public CamerasPage(CamerasViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
