using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using CommandType = HomeManagement.Shared.CommandType;
using HomeManagement.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Application.Dashboard;

/// <summary>
/// The current value of a property a widget shows.
/// </summary>
/// <param name="Value">The raw value, or null when the output does not have the property.</param>
public sealed record DashboardWidgetPropertyValue(DashboardWidgetProperty Property, string? Value);

/// <summary>
/// What a widget shows now.
/// </summary>
/// <param name="Output">The raw output when the widget shows all of it, or the error of a failed command.</param>
/// <param name="Properties">The values of the chosen properties, empty when the widget shows the whole output or there is no output.</param>
/// <param name="IsError">The output is an error the device answered a command with.</param>
/// <param name="Message">Why there is nothing to show.</param>
public sealed record DashboardWidgetValue(
    DevicePresence Presence,
    string? Output,
    IReadOnlyList<DashboardWidgetPropertyValue> Properties,
    bool IsError,
    DateTimeOffset? UpdatedAtUtc,
    string? Message);

/// <summary>
/// The current output of an action, so the widget editor can offer its JSON properties.
/// </summary>
/// <param name="Message">Why there is no output yet, or null when there is.</param>
public sealed record DashboardWidgetPreview(
    string? RawOutput,
    IReadOnlyList<WorkflowTriggerPropertyOption> Properties,
    string? Message);

/// <summary>
/// Device action outputs pinned to the home screen. The values are read from the live device state, so reading them is cheap.
/// </summary>
public sealed class DashboardWidgetService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    DeviceStateStore stateStore)
{
    /// <summary>
    /// Raised when a widget is added, changed, moved or deleted.
    /// </summary>
    public event Action? WidgetsChanged;

    public async Task<List<DashboardWidget>> ListAsync(CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        return await db.DashboardWidgets
            .Include(x => x.DeviceAction)
            .ThenInclude(x => x.Device)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Id)
            .ToListAsync(token);
    }

    /// <summary>
    /// How many widgets show actions of the device. They are removed with the device.
    /// </summary>
    public async Task<int> CountForDeviceAsync(int deviceId, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        return await db.DashboardWidgets.CountAsync(x => x.DeviceAction.DeviceId == deviceId, token);
    }

    public static string GetTitle(DashboardWidget widget)
        => string.IsNullOrWhiteSpace(widget.Title) ? DisplayFormatter.FormatActionName(widget.DeviceAction.Action) : widget.Title;

    /// <summary>
    /// The latest value of the widget. Values of a device that went offline stay, so the widget shows the last known one.
    /// </summary>
    public DashboardWidgetValue Read(DashboardWidget widget)
    {
        var action = widget.DeviceAction;
        var device = action.Device;
        var presence = stateStore.GetPresence(device.DeviceId);
        var (output, isError, updatedAt) = ReadOutput(device, action);
        if (output is null)
        {
            return new DashboardWidgetValue(presence, null, [], false, null, action.CommandType switch
            {
                CommandType.Post => Strings.Widget_RunToSeeOutput,
                // Events are not retained, so the hub only knows the ones that happened since it started.
                CommandType.Event => string.Format(Strings.Widget_NoEventSinceStart, action.Action),
                _ => string.Format(Strings.Action_NotReportedYet, device.Name, action.Action)
            });
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            // E.g. a command that only switches something on.
            return new DashboardWidgetValue(presence, null, [], isError, updatedAt, isError ? Strings.Widget_FailedNoReason : Strings.Widget_NoOutput);
        }

        if (isError || widget.Properties.Count == 0)
        {
            return new DashboardWidgetValue(presence, output, [], isError, updatedAt, null);
        }

        List<DashboardWidgetPropertyValue> values =
        [
            .. widget.Properties.Select(property => new DashboardWidgetPropertyValue(
                property,
                JsonValueReader.TryReadProperty(output, property.Path, out var value, out _) ? value ?? "null" : null))
        ];
        return new DashboardWidgetValue(
            presence,
            null,
            values,
            false,
            updatedAt,
            values.All(x => x.Value is null) ? Strings.Widget_NoneOfValues : null);
    }

    /// <summary>
    /// Reads the current output of an action without running it, because running a command may change something, e.g. restart a device.
    /// </summary>
    public async Task<DashboardWidgetPreview> PreviewAsync(int actionId, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (await db.Set<DeviceAction>().Include(x => x.Device).FirstOrDefaultAsync(x => x.Id == actionId, token) is not { } action)
        {
            return new DashboardWidgetPreview(null, [], Strings.Widget_ActionGone);
        }

        var (output, isError, _) = ReadOutput(action.Device, action);
        return output switch
        {
            null when action.CommandType == CommandType.Post => new DashboardWidgetPreview(null, [], Strings.Widget_RunToSeeProperties),
            null => new DashboardWidgetPreview(null, [], string.Format(Strings.Widget_PropertiesUnknownNotReported, action.Device.Name, action.Action)),
            _ when isError => new DashboardWidgetPreview(output, [], Strings.Widget_PropertiesUnknownFailed),
            _ => new DashboardWidgetPreview(output, JsonValueReader.GetProperties(output), null)
        };
    }

    public async Task<OperationResult> AddAsync(DashboardWidget widget, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (!await db.Set<DeviceAction>().AnyAsync(x => x.Id == widget.DeviceActionId, token))
        {
            return OperationResult.NotFound(Strings.Widget_ActionGone);
        }

        Normalize(widget);
        widget.Order = await db.DashboardWidgets.MaxAsync(x => (int?)x.Order, token) + 1 ?? 0;
        db.DashboardWidgets.Add(widget);
        await db.SaveChangesAsync(token);
        WidgetsChanged?.Invoke();
        return OperationResult.Success(Strings.Widget_Added);
    }

    public async Task<OperationResult> UpdateAsync(int id, DashboardWidget updated, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var widget = await db.DashboardWidgets.AsTracking().FirstOrDefaultAsync(x => x.Id == id, token);
        if (widget is null)
        {
            return OperationResult.NotFound(Strings.Widget_NotFound);
        }

        if (!await db.Set<DeviceAction>().AnyAsync(x => x.Id == updated.DeviceActionId, token))
        {
            return OperationResult.NotFound(Strings.Widget_ActionGone);
        }

        Normalize(updated);
        widget.DeviceActionId = updated.DeviceActionId;
        widget.Title = updated.Title;
        widget.Properties = updated.Properties;
        await db.SaveChangesAsync(token);
        WidgetsChanged?.Invoke();
        return OperationResult.Success(Strings.Widget_Updated);
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        if (await db.DashboardWidgets.Where(x => x.Id == id).ExecuteDeleteAsync(token) == 0)
        {
            return OperationResult.NotFound(Strings.Widget_NotFound);
        }

        WidgetsChanged?.Invoke();
        return OperationResult.Success(Strings.Widget_Removed);
    }

    /// <summary>
    /// Moves the widget <paramref name="offset"/> places, e.g. -1 to show it one place earlier.
    /// </summary>
    public async Task<OperationResult> MoveAsync(int id, int offset, CancellationToken token = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(token);
        var widgets = await db.DashboardWidgets.AsTracking().OrderBy(x => x.Order).ThenBy(x => x.Id).ToListAsync(token);
        var index = widgets.FindIndex(x => x.Id == id);
        if (index < 0)
        {
            return OperationResult.NotFound(Strings.Widget_NotFound);
        }

        var widget = widgets[index];
        widgets.RemoveAt(index);
        widgets.Insert(Math.Clamp(index + offset, 0, widgets.Count), widget);
        foreach (var (order, item) in widgets.Index())
        {
            item.Order = order;
        }

        await db.SaveChangesAsync(token);
        WidgetsChanged?.Invoke();
        return OperationResult.Success(Strings.Widget_Moved);
    }

    private (string? Output, bool IsError, DateTimeOffset? UpdatedAtUtc) ReadOutput(Device device, DeviceAction action)
    {
        if (action.CommandType == CommandType.Post)
        {
            return stateStore.GetCommandOutput(device.DeviceId, action.Command) is { } output
                ? (output.Output, !output.IsSuccess, output.ReceivedAtUtc)
                : (null, false, null);
        }

        return stateStore.GetTelemetry(device.DeviceId, action.Command) is { } telemetry
            ? (telemetry.Payload, false, telemetry.ReceivedAtUtc)
            : (null, false, null);
    }

    private static void Normalize(DashboardWidget widget)
    {
        widget.Title = string.IsNullOrWhiteSpace(widget.Title) ? null : widget.Title.Trim();
        widget.Properties =
        [
            .. widget.Properties
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .DistinctBy(x => x.Path.Trim(), StringComparer.Ordinal)
                .Select(x => new DashboardWidgetProperty(
                    x.Path.Trim(),
                    string.IsNullOrWhiteSpace(x.Label) ? null : x.Label.Trim(),
                    string.IsNullOrWhiteSpace(x.Unit) ? null : x.Unit.Trim()))
        ];
    }
}
