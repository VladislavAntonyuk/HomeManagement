using System.Text.Json;
using System.Text.Json.Serialization;
using HomeManagement.Shared.Api;

namespace HomeManagementClient.Services;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DashboardDto))]
[JsonSerializable(typeof(OperationResultDto))]
[JsonSerializable(typeof(SetEnabledRequest))]
[JsonSerializable(typeof(List<DeviceDto>))]
[JsonSerializable(typeof(List<WorkflowDto>))]
[JsonSerializable(typeof(List<CameraDto>))]
[JsonSerializable(typeof(ProblemDetailsDto))]
public sealed partial class ApiJsonContext : JsonSerializerContext;

public sealed record ProblemDetailsDto(string? Title, string? Detail, int? Status);
