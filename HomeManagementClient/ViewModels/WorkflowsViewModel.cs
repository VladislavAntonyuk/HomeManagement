using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Shared.Api;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public sealed partial class WorkflowsViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    public ObservableCollection<WorkflowItemViewModel> Workflows { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var workflows = await api.GetWorkflowsAsync(token);
        var existing = Workflows.ToDictionary(x => x.Id);
        if (!workflows.Select(x => x.Id).SequenceEqual(Workflows.Select(x => x.Id)))
        {
            Workflows.Clear();
            foreach (var workflow in workflows)
            {
                Workflows.Add(existing.TryGetValue(workflow.Id, out var item) ? item : new WorkflowItemViewModel(workflow, api, Notifier));
            }
        }

        foreach (var workflow in workflows)
        {
            Workflows.First(x => x.Id == workflow.Id).Update(workflow);
        }

        Summary = $"{workflows.Count(x => x.IsEnabled)} of {workflows.Count} enabled";
    }
}

public sealed partial class WorkflowItemViewModel : ObservableObject
{
    private readonly IHomeManagementApi _api;
    private readonly UserNotifier _notifier;
    private bool _isApplyingServerState;

    public WorkflowItemViewModel(WorkflowDto workflow, IHomeManagementApi api, UserNotifier notifier)
    {
        _api = api;
        _notifier = notifier;
        Id = workflow.Id;
        Update(workflow);
    }

    public int Id { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Description { get; private set; }

    [ObservableProperty]
    public partial string Trigger { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Details { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsUpdating { get; private set; }

    public void Update(WorkflowDto workflow)
    {
        Name = workflow.Name;
        Description = string.IsNullOrWhiteSpace(workflow.Description) ? null : workflow.Description;
        Trigger = workflow.Trigger;
        var steps = workflow.Steps == 1 ? "1 step" : $"{workflow.Steps} steps";
        var repeat = workflow.RepeatWhileMatched ? " · repeats while matched" : string.Empty;
        Details = $"{steps}{repeat} · last run {DisplayFormatter.FormatAgo(workflow.LastTriggeredAtUtc)}";
        if (!IsUpdating)
        {
            _isApplyingServerState = true;
            IsEnabled = workflow.IsEnabled;
            _isApplyingServerState = false;
        }
    }

    async partial void OnIsEnabledChanged(bool value)
    {
        if (_isApplyingServerState)
        {
            return;
        }

        IsUpdating = true;
        try
        {
            var result = await _api.SetWorkflowEnabledAsync(Id, new SetEnabledRequest(value));
            await _notifier.ShowAsync(result.Message, !result.IsSuccess);
        }
        catch (Exception e)
        {
            _isApplyingServerState = true;
            IsEnabled = !value;
            _isApplyingServerState = false;
            await _notifier.ShowErrorAsync(e);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (!await _notifier.ConfirmAsync("Run workflow", $"Run '{Name}' now, regardless of its trigger?", "Run"))
        {
            return;
        }

        try
        {
            var result = await _api.RunWorkflowAsync(Id);
            await _notifier.ShowAsync(result.Message, !result.IsSuccess);
        }
        catch (Exception e)
        {
            await _notifier.ShowErrorAsync(e);
        }
    }
}
