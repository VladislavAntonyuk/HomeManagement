namespace HomeManagementClient.Pages;

public sealed class PlatformTemplateSelector : DataTemplateSelector
{
    public DataTemplate Default { get; set; } = null!;

    public DataTemplate Linux { get; set; } = null!;

#if GTK
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => Linux;
#else
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => Default;
#endif
}
