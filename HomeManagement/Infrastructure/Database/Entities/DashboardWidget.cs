namespace HomeManagement.Infrastructure.Database.Entities;

/// <summary>
/// The output of a device action, pinned to the home screen.
/// </summary>
public class DashboardWidget
{
    public int Id { get; init; }

    public int DeviceActionId { get; set; }

    public DeviceAction DeviceAction { get; set; } = null!;

    /// <summary>
    /// Shown instead of the action name, or null to show the action name.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The values of the output to show, in this order. Empty shows the whole output.
    /// </summary>
    public List<DashboardWidgetProperty> Properties { get; set; } = [];

    /// <summary>
    /// Position of the widget on the home screen.
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// A value of the output a widget shows. Stored as JSON in the widget row.
/// </summary>
/// <param name="Path">JSON path of the value, e.g. "$.temperature".</param>
/// <param name="Label">Shown before the value, or null to show a label made from the path.</param>
/// <param name="Unit">Shown after the value, e.g. "°C".</param>
public sealed record class DashboardWidgetProperty(string Path, string? Label, string? Unit);
