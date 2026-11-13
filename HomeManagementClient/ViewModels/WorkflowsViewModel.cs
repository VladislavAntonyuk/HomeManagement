using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Shared.Api;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

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

        Summary = $"{workflows.Count(x => x.IsEnabled)} of {workflows.Count} enabled";
    }
}

public sealed partial class WorkflowItemViewModel : ObservableObject
{
    private readonly IHomeManagementApi _api;
    private readonly UserNotifier _notifier;
    private bool _isApplyingServerState;
    private bool _isRunConfirmationPending;

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

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TriggerText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EnabledText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ToggleText { get; private set; } = string.Empty;

    [RelayCommand]
    private void ToggleEnabled() => IsEnabled = !IsEnabled;

    public void Update(WorkflowDto workflow)
    {
        Name = workflow.Name;
        Description = string.IsNullOrWhiteSpace(workflow.Description) ? null : workflow.Description;
        Trigger = workflow.Trigger;
        TriggerText = $"When {workflow.Trigger}";
        var steps = workflow.Steps == 1 ? "1 step" : $"{workflow.Steps} steps";
        var repeat = workflow.RepeatWhileMatched ? " · repeats while matched" : string.Empty;
        Details = $"{steps}{repeat} · last run {DisplayFormatter.FormatAgo(workflow.LastTriggeredAtUtc)}";
        SummaryText = Description is null ? Details : $"{Description} · {Details}";
        if (!IsUpdating)
        {
            _isApplyingServerState = true;
            IsEnabled = workflow.IsEnabled;
            _isApplyingServerState = false;
        }

        EnabledText = IsEnabled ? "Enabled" : "Disabled";
        ToggleText = IsEnabled ? "Disable" : "Enable";
    }

    async partial void OnIsEnabledChanged(bool value)
    {
        EnabledText = value ? "Enabled" : "Disabled";
        ToggleText = value ? "Disable" : "Enable";
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
        if (UserNotifier.UsesInlineResults)
        {
            if (!_isRunConfirmationPending)
            {
                _isRunConfirmationPending = true;
                _notifier.ShowProgress($"Press Run now again to run '{Name}', regardless of its trigger.");
                return;
            }

            _isRunConfirmationPending = false;
        }
        else if (!await _notifier.ConfirmAsync("Run workflow", $"Run '{Name}' now, regardless of its trigger?", "Run"))
        {
            return;
        }

        try
        {
            _notifier.ShowProgress($"'{Name}': running...");
            var result = await _api.RunWorkflowAsync(Id);
            await _notifier.ShowAsync(result.Message, !result.IsSuccess);
        }
        catch (Exception e)
        {
            await _notifier.ShowErrorAsync(e);
        }
    }
}
