using HomeManagement.Application.Dashboard;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using CommandType = HomeManagement.Shared.CommandType;

namespace HomeManagement.Components.Dialogs;

public partial class DashboardWidgetEditDialog(
    DashboardWidgetService widgetService,
    DeviceService deviceService,
    ISnackbar snackbar)
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;

    [Parameter] public DashboardWidgetEditModel Model { get; set; } = new();

    [Parameter] public IReadOnlyList<Device> Devices { get; set; } = [];

    private MudForm _form = default!;
    private DashboardWidgetPreview? _preview;
    private bool _isRunning;

    private IEnumerable<DeviceAction> Actions => Devices.FirstOrDefault(x => x.Id == Model.DeviceId)?.Actions ?? [];

    private DeviceAction? SelectedAction => Actions.FirstOrDefault(x => x.Id == Model.ActionId);

    private IReadOnlyCollection<string> SelectedPaths => [.. Model.Properties.Select(x => x.Path)];

    /// <summary>
    /// The properties of the current output, plus chosen ones it does not have, e.g. before the device reported again.
    /// </summary>
    private IEnumerable<(string Path, string Sample)> PropertyOptions
    {
        get
        {
            var properties = _preview?.Properties ?? [];
            return properties
                .Select(x => (x.Path, x.SampleValue ?? "null"))
                .Concat(Model.Properties
                    .Where(x => properties.All(p => p.Path != x.Path))
                    .Select(x => (x.Path, Strings.WidgetEdit_NotInOutput)));
        }
    }

    protected override async Task OnInitializedAsync() => await LoadPreviewAsync();

    private async Task OnDeviceChanged(int? deviceId)
    {
        Model.DeviceId = deviceId;
        Model.ActionId = Actions.Count() == 1 ? Actions.First().Id : null;
        Model.Properties.Clear();
        await LoadPreviewAsync();
    }

    private async Task OnActionChanged(int? actionId)
    {
        Model.ActionId = actionId;
        Model.Properties.Clear();
        await LoadPreviewAsync();
    }

    /// <summary>
    /// Keeps the order and the labels of the properties that stay chosen, and adds the new ones at the end.
    /// </summary>
    private void OnPropertiesChanged(IReadOnlyCollection<string>? paths)
    {
        List<string> selected = [.. paths ?? []];
        Model.Properties.RemoveAll(x => !selected.Contains(x.Path));
        foreach (var path in selected.Where(path => Model.Properties.All(x => x.Path != path)))
        {
            Model.Properties.Add(new DashboardWidgetPropertyEditModel { Path = path, Unit = UnitSuggestion(path) });
        }
    }

    private void Move(DashboardWidgetPropertyEditModel property, int offset)
    {
        var index = Model.Properties.IndexOf(property);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < Model.Properties.Count)
        {
            (Model.Properties[index], Model.Properties[target]) = (Model.Properties[target], Model.Properties[index]);
        }
    }

    private string PropertiesText(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => Strings.WidgetEdit_WholeOutput,
        _ => string.Join(", ", Model.Properties.Select(x => DisplayFormatter.FormatPropertyLabel(x.Path, x.Label)))
    };

    /// <summary>
    /// Reads the output the hub already has. A Post action is not run, because running it may change something.
    /// </summary>
    private async Task LoadPreviewAsync()
    {
        _preview = null;
        if (Model.ActionId is not { } actionId)
        {
            return;
        }

        _preview = await widgetService.PreviewAsync(actionId);
    }

    private async Task RunActionAsync()
    {
        if (Model.DeviceId is not { } deviceId || Model.ActionId is not { } actionId)
        {
            return;
        }

        _isRunning = true;
        try
        {
            var result = await deviceService.RunActionAsync(deviceId, actionId);
            if (!result.IsSuccess)
            {
                snackbar.Add(result.Message, Severity.Error);
            }

            await LoadPreviewAsync();
        }
        finally
        {
            _isRunning = false;
        }
    }

    private async Task Save()
    {
        await _form.ValidateAsync();
        if (!_form.IsValid)
        {
            return;
        }

        DialogReference.Close(DialogResult.Ok(Model));
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());

    private string DeviceName(int? id) => Devices.FirstOrDefault(x => x.Id == id)?.Name ?? string.Empty;

    private string ActionName(int? id) => Actions.FirstOrDefault(x => x.Id == id) is { } action ? DisplayFormatter.FormatActionName(action.Action) : string.Empty;

    private static string CommandTypeName(CommandType type) => type switch
    {
        CommandType.Get => Strings.CommandType_Get,
        CommandType.Event => Strings.CommandType_Event,
        _ => Strings.CommandType_Post
    };

    private static string? UnitSuggestion(string path)
    {
        var name = path[(path.LastIndexOfAny(['.', ']']) + 1)..].ToLowerInvariant();
        return name switch
        {
            _ when name.Contains("temperature") || name is "temp" => "°C",
            _ when name.Contains("humidity") => "%",
            _ when name.Contains("percent") || name is "battery" or "soc" => "%",
            _ when name.Contains("power") || name.EndsWith("watts") => "W",
            _ when name.Contains("voltage") => "V",
            _ => null
        };
    }
}
