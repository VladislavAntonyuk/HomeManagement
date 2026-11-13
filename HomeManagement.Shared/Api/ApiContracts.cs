using System.Text.Json.Serialization;

namespace HomeManagement.Shared.Api;

public static class ApiRoutes
{
    public const string ApiKeyHeader = "X-API-Key";

    public const string Dashboard = "/api/dashboard";
    public const string AwayMode = "/api/away-mode";
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

public sealed record OperationResultDto(bool IsSuccess, string Message);

public sealed record SetEnabledRequest(bool Enabled);

public sealed record HubInfoDto(string MachineName, string? LocalIp, string OperatingSystem, long UptimeSeconds, double? TemperatureCelsius);

public sealed record DashboardDto(
    bool AwayModeEnabled,
    int TotalDevices,
    int OnlineDevices,
    int OfflineDevices,
    int NotRespondingDevices,
    int TotalWorkflows,
    int EnabledWorkflows,
    DateTimeOffset? LastWorkflowRunAtUtc,
    HubInfoDto Hub);

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
