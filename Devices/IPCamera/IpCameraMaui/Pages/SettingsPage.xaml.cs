using IpCameraMaui.ViewModels;

namespace IpCameraMaui.Pages;

public partial class SettingsPage
{
	public SettingsPage(SettingsViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}