namespace HomeManagementClient.Pages;

public partial class WorkflowsPage : ContentPage
{
    public WorkflowsPage(WorkflowsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
#if GTK
        var items = StaticItemsView.Replace(ItemsList, "No workflows");
        items.SetBinding(StaticItemsView.ItemsSourceProperty, static (WorkflowsViewModel vm) => vm.Workflows);
        items.SetBinding(StaticItemsView.VersionProperty, static (WorkflowsViewModel vm) => vm.LoadVersion);
#endif
    }
}
