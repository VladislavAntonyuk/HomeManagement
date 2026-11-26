using System.Text.Json.Serialization;
using HomeManagement.Shared.Resources;

namespace HomeManagement.Shared.Api;

public static class ApiRoutes
{
    public const string ApiKeyHeader = "X-API-Key";

    public const string Dashboard = "/api/dashboard";
    public const string DashboardWidgets = "/api/dashboard/widgets";
    public const string Devices = "/api/devices";
    public const string DeviceAction = "/api/devices/{deviceId}/actions/{actionId}";
    public const string Workflows = "/api/workflows";
    public const string WorkflowRun = "/api/workflows/{workflowId}/run";
    public const string WorkflowEnabled = "/api/workflows/{workflowId}/enabled";
    public const string Cameras = "/api/cameras";
    public const string CameraSnapshot = "/api/cameras/{camera}/snapshot";
    public const string CameraStream = "/api/cameras/{camera}/stream";
}

[JsonConverter(typeof(JsonStringEnumConverter<DevicePresenceStatus>))]
public enum DevicePresenceStatus
{
    Unknown,
    Online,
    Offline,

    /// <summary>
    /// The device reports online but stopped sending heartbeats.
    /// </summary>
    NotResponding
}

public static class DevicePresenceStatusExtensions
{
    extension(DevicePresenceStatus status)
    {
        public string DisplayName => status switch
        {
            DevicePresenceStatus.Online => SharedStrings.StatusOnline,
            DevicePresenceStatus.Offline => SharedStrings.StatusOffline,
            DevicePresenceStatus.NotResponding => SharedStrings.StatusNotResponding,
            _ => SharedStrings.StatusUnknown
        };
    }
}

/// <summary>
/// What a successful command did. Failures are answered with a problem details body and an error status code.
/// </summary>
public sealed record OperationResultDto(string Message);

public sealed record SetEnabledRequest(bool Enabled);

public sealed record HubInfoDto(string MachineName, string? LocalIp, string OperatingSystem, long UptimeSeconds, double? TemperatureCelsius);

public sealed record DashboardDto(
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    int NotRespondingDevices,
    int TotalWorkflows,
    int EnabledWorkflows,
    DateTimeOffset? LastWorkflowRunAtUtc,
    HubInfoDto Hub);

/// <summary>
/// A value of the output a widget shows.
/// </summary>
/// <param name="Path">JSON path of the value, e.g. "$.temperature".</param>
/// <param name="Label">The label the user gave the value, or null to make one from the path.</param>
/// <param name="Value">The raw value, or null when the output does not have it.</param>
public sealed record DashboardWidgetPropertyDto(string Path, string? Label, string? Unit, string? Value);

/// <summary>
/// The output of a device action, pinned to the home screen.
/// </summary>
/// <param name="Title">The title the user gave the widget, or the action name.</param>
/// <param name="Output">The raw output when the widget shows all of it, or the error of a failed command.</param>
/// <param name="Properties">The values the widget shows, in order. Empty when it shows the whole output, or there is no output.</param>
/// <param name="IsError">The output is an error the device answered a command with.</param>
/// <param name="UpdatedAtUtc">When the device reported the output.</param>
/// <param name="Message">Why there is nothing to show, e.g. the device has not reported the value yet.</param>
public sealed record DashboardWidgetDto(
    int Id,
    string Title,
    int DeviceId,
    string DeviceName,
    int ActionId,
    string ActionName,
    CommandType CommandType,
    DevicePresenceStatus Status,
    string? Output,
    IReadOnlyList<DashboardWidgetPropertyDto> Properties,
    bool IsError,
    DateTimeOffset? UpdatedAtUtc,
    string? Message);

public sealed record DeviceActionDto(int Id, string Name, CommandType CommandType);

public sealed record DeviceMetricDto(string Name, string Value, string Label, string DisplayValue);

public sealed record DeviceDto(
    int Id,
    string Name,
    string DeviceId,
    string? Description,
    DevicePresenceStatus Status,
    DateTimeOffset? LastSeenUtc,
    string? Address,
    IReadOnlyList<DeviceMetricDto> Metrics,
    IReadOnlyList<DeviceActionDto> Actions);

public sealed record WorkflowDto(
    int Id,
    string Name,
    string? Description,
    bool IsEnabled,
    bool RepeatWhileMatched,
    string Trigger,
    int Steps,
    DateTimeOffset? LastTriggeredAtUtc);

public sealed record CameraDto(string Name);
