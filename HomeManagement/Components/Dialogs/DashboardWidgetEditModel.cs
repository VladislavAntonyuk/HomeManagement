using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Dialogs;

public sealed class DashboardWidgetEditModel
{
    public int? DeviceId { get; set; }

    public int? ActionId { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// The values to show, in order. Empty shows the whole output.
    /// </summary>
    public List<DashboardWidgetPropertyEditModel> Properties { get; set; } = [];

    public static DashboardWidgetEditModel From(DashboardWidget widget) => new()
    {
        DeviceId = widget.DeviceAction.DeviceId,
        ActionId = widget.DeviceActionId,
        Title = widget.Title,
        Properties = [.. widget.Properties.Select(x => new DashboardWidgetPropertyEditModel { Path = x.Path, Label = x.Label, Unit = x.Unit })]
    };

    public DashboardWidget ToEntity() => new()
    {
        DeviceActionId = ActionId ?? throw new InvalidOperationException("Choose an action first."),
        Title = Title,
        Properties = [.. Properties.Select(x => new DashboardWidgetProperty(x.Path, x.Label, x.Unit))]
    };
}

public sealed class DashboardWidgetPropertyEditModel
{
    public required string Path { get; init; }

    public string? Label { get; set; }

    public string? Unit { get; set; }
}
