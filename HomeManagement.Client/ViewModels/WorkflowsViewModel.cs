using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Shared.Api;
using HomeManagement.Client.Services;

namespace HomeManagement.Client.ViewModels;

public sealed partial class WorkflowsViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    [ObservableProperty]
    public partial IReadOnlyList<WorkflowItemViewModel> Workflows { get; private set; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var workflows = await api.GetWorkflowsAsync(token);
        var existing = Workflows.ToDictionary(x => x.Id);
        if (!workflows.Select(x => x.Id).SequenceEqual(Workflows.Select(x => x.Id)))
        {
            Workflows = workflows
                .Select(x => existing.TryGetValue(x.Id, out var item) ? item : new WorkflowItemViewModel(x, api, Notifier))
                .ToList();
        }

        foreach (var workflow in workflows)
        {
            Workflows.First(x => x.Id == workflow.Id).Update(workflow);
        }

        Summary = string.Format(CultureInfo.CurrentCulture, AppResources.WorkflowsSummary, workflows.Count(x => x.IsEnabled), workflows.Count);
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
        var steps = workflow.Steps == 1 ? AppResources.WorkflowStepsOne : string.Format(CultureInfo.CurrentCulture, AppResources.WorkflowStepsMany, workflow.Steps);
        var repeat = workflow.RepeatWhileMatched ? $" · {AppResources.WorkflowRepeats}" : string.Empty;
        var lastRun = string.Format(CultureInfo.CurrentCulture, AppResources.WorkflowLastRun, DisplayFormatter.FormatAgo(workflow.LastTriggeredAtUtc, DateTimeOffset.UtcNow));
        Details = $"{steps}{repeat} · {lastRun}";
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
            await _notifier.ShowAsync(result.Message);
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
        if (!await _notifier.ConfirmAsync(AppResources.RunWorkflowTitle, string.Format(CultureInfo.CurrentCulture, AppResources.RunWorkflowMessage, Name), AppResources.Run))
        {
            return;
        }

        try
        {
            var result = await _api.RunWorkflowAsync(Id);
            await _notifier.ShowAsync(result.Message);
        }
        catch (Exception e)
        {
            await _notifier.ShowErrorAsync(e);
        }
    }
}
