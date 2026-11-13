using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Shared;
using HomeManagement.Shared.Api;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public sealed partial class DevicesViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    private readonly Dictionary<int, DeviceItemViewModel> _devices = [];

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var devices = await api.GetDevicesAsync(token);
        foreach (var removed in _devices.Keys.Except(devices.Select(x => x.Id)).ToList())
        {
            _devices.Remove(removed);
        }

        foreach (var device in devices)
        {
            if (_devices.TryGetValue(device.Id, out var item))
            {
                item.Update(device);
            }
            else
            {
                _devices[device.Id] = new DeviceItemViewModel(device, api, Notifier);
            }
        }

        var online = devices.Count(x => x.Status == DevicePresenceStatus.Online);
        Summary = $"{online} of {devices.Count} online";
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _devices.Values
            .Where(x => string.IsNullOrWhiteSpace(SearchText) || x.Matches(SearchText.Trim()))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            if (!filtered.Contains(Devices[i]))
            {
                Devices.RemoveAt(i);
            }
        }

        for (var i = 0; i < filtered.Count; i++)
        {
            var index = Devices.IndexOf(filtered[i]);
            if (index < 0)
            {
                Devices.Insert(i, filtered[i]);
            }
            else if (index != i)
            {
                Devices.Move(index, i);
            }
        }
    }
}

public sealed partial class DeviceItemViewModel : ObservableObject
{
    private readonly IHomeManagementApi _api;
    private readonly UserNotifier _notifier;

    public DeviceItemViewModel(DeviceDto device, IHomeManagementApi api, UserNotifier notifier)
    {
        _api = api;
        _notifier = notifier;
        Id = device.Id;
        Update(device);
    }

    public int Id { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DeviceId { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Description { get; private set; }

    [ObservableProperty]
    public partial DevicePresenceStatus Status { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastSeen { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Address { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DeviceMetricViewModel> Metrics { get; private set; } = [];

    public const int ActionsPerRow = 3;

    [ObservableProperty]
    public partial IReadOnlyList<DeviceActionItemViewModel> Actions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<DeviceActionRow> ActionRows { get; private set; } = [];

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string MetricsText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial DeviceActionItemViewModel? SelectedAction { get; set; }

    [RelayCommand]
    private async Task RunSelectedActionAsync()
    {
        if ((SelectedAction ?? Actions.FirstOrDefault()) is { } action)
        {
            _notifier.ShowProgress($"{Name}: {action.Name}: running...");
            await action.RunCommand.ExecuteAsync(null);
        }
    }

    public void Update(DeviceDto device)
    {
        Name = device.Name;
        DeviceId = device.DeviceId;
        Description = string.IsNullOrWhiteSpace(device.Description) ? null : device.Description;
        Status = device.Status;
        StatusText = DisplayFormatter.FormatStatus(device.Status);
        LastSeen = $"Last seen {DisplayFormatter.FormatAgo(device.LastSeenUtc)}";
        Address = device.Address;
        SummaryText = Address is null ? LastSeen : $"{LastSeen} · {Address}";
        MetricsText = string.Join(" · ", device.Metrics.Select(x => $"{x.Label} {x.DisplayValue}"));
        if (Metrics.Select(x => x.Name).SequenceEqual(device.Metrics.Select(x => x.Name)))
        {
            for (var i = 0; i < Metrics.Count; i++)
            {
                Metrics[i].Update(device.Metrics[i]);
            }
        }
        else
        {
            Metrics = device.Metrics.Select(x => new DeviceMetricViewModel(x)).ToList();
        }

        if (!Actions.Select(x => (x.Id, x.Name, x.CommandType)).SequenceEqual(device.Actions.Select(x => (x.Id, x.Name, x.CommandType))))
        {
            Actions = device.Actions.Select((x, i) => new DeviceActionItemViewModel(this, x, _api, _notifier, i % ActionsPerRow)).ToList();
            ActionRows = Actions.Chunk(ActionsPerRow).Select(x => new DeviceActionRow(x)).ToList();
            SelectedAction = Actions.FirstOrDefault();
        }
    }

    public bool Matches(string text)
        => Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
           || DeviceId.Contains(text, StringComparison.CurrentCultureIgnoreCase)
           || Description?.Contains(text, StringComparison.CurrentCultureIgnoreCase) == true;
}

public sealed record DeviceActionRow(IReadOnlyList<DeviceActionItemViewModel> Actions);

public sealed partial class DeviceActionItemViewModel(
    DeviceItemViewModel device,
    DeviceActionDto action,
    IHomeManagementApi api,
    UserNotifier notifier,
    int column) : ObservableObject
{
    public int Id => action.Id;

    public int Column => column;

    public string Name => action.Name;

    public CommandType CommandType => action.CommandType;

    public bool IsReadOnly => action.CommandType == CommandType.Get;

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            var result = await api.RunDeviceActionAsync(device.Id, action.Id);
            var message = DeviceActionResultFormatter.Format(result.Message);
            if (IsReadOnly && result.IsSuccess)
            {
                await notifier.ShowAsync(message, title: $"{device.Name}: {action.Name}");
            }
            else
            {
                await notifier.ShowAsync(message, !result.IsSuccess, UserNotifier.UsesInlineResults ? $"{device.Name}: {action.Name}" : null);
            }
        }
        catch (Exception e)
        {
            await notifier.ShowErrorAsync(e);
        }
    }
}

public sealed partial class DeviceMetricViewModel : ObservableObject
{
    public DeviceMetricViewModel(DeviceMetricDto metric)
    {
        Name = metric.Name;
        Update(metric);
    }

    public string Name { get; }

    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayValue { get; private set; } = string.Empty;

    public void Update(DeviceMetricDto metric)
    {
        Label = metric.Label;
        DisplayValue = metric.DisplayValue;
    }
}
