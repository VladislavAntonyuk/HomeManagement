namespace HomeManagement.Client.Pages;

public partial class WorkflowsPage : ContentPage
{
    public WorkflowsPage(WorkflowsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
