namespace HomeManagementClient.Pages;

public partial class WorkflowsPage : ContentPage
{
    public WorkflowsPage(WorkflowsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
