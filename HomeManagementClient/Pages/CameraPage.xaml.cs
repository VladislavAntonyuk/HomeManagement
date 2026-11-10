namespace HomeManagementClient.Pages;

public partial class CameraPage : ContentPage
{
    public CameraPage(CameraViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
