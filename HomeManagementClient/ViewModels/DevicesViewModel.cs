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

        // Update in place so the list keeps its scroll position and running actions
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
    public partial IReadOnlyList<DeviceMetricDto> Metrics { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<DeviceActionItemViewModel> Actions { get; private set; } = [];

    public void Update(DeviceDto device)
    {
        Name = device.Name;
        DeviceId = device.DeviceId;
        Description = string.IsNullOrWhiteSpace(device.Description) ? null : device.Description;
        Status = device.Status;
        StatusText = DisplayFormatter.FormatStatus(device.Status);
        LastSeen = $"Last seen {DisplayFormatter.FormatAgo(device.LastSeenUtc)}";
        Address = device.Address;
        if (!Metrics.SequenceEqual(device.Metrics))
        {
            Metrics = device.Metrics;
        }

        if (!Actions.Select(x => (x.Id, x.Name, x.CommandType)).SequenceEqual(device.Actions.Select(x => (x.Id, x.Name, x.CommandType))))
        {
            Actions = device.Actions.Select(x => new DeviceActionItemViewModel(this, x, _api, _notifier)).ToList();
        }
    }

    public bool Matches(string text)
        => Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
           || DeviceId.Contains(text, StringComparison.CurrentCultureIgnoreCase)
           || Description?.Contains(text, StringComparison.CurrentCultureIgnoreCase) == true;
}

public sealed partial class DeviceActionItemViewModel(
    DeviceItemViewModel device,
    DeviceActionDto action,
    IHomeManagementApi api,
    UserNotifier notifier) : ObservableObject
{
    public int Id => action.Id;

    public string Name => action.Name;

    public CommandType CommandType => action.CommandType;

    /// <summary>
    /// Get actions read the last value the device reported, Post actions send a command.
    /// </summary>
    public bool IsReadOnly => action.CommandType == CommandType.Get;

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            var result = await api.RunDeviceActionAsync(device.Id, action.Id);
            if (IsReadOnly && result.IsSuccess)
            {
                await notifier.ShowAsync(result.Message, title: $"{device.Name}: {action.Name}");
            }
            else
            {
                await notifier.ShowAsync(result.Message, !result.IsSuccess);
            }
        }
        catch (Exception e)
        {
            await notifier.ShowErrorAsync(e);
        }
    }
}
