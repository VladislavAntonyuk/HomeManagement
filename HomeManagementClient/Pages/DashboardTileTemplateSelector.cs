namespace HomeManagementClient.Pages;

public sealed class DashboardTileTemplateSelector : DataTemplateSelector
{
    public DataTemplate Devices { get; set; } = null!;

    public DataTemplate Workflows { get; set; } = null!;

    public DataTemplate Hub { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => (item as DashboardTile)?.Kind switch
    {
        DashboardTileKind.Devices => Devices,
        DashboardTileKind.Workflows => Workflows,
        _ => Hub
    };
}
