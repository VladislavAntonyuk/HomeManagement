using HomeManagement.Shared.Api;
using Refit;

namespace HomeManagementClient.Services;

public interface IHomeManagementApi
{
    [Get(ApiRoutes.Dashboard)]
    Task<DashboardDto> GetDashboardAsync(CancellationToken token = default);

    [Put(ApiRoutes.AwayMode)]
    Task<OperationResultDto> SetAwayModeAsync([Body] SetEnabledRequest request, CancellationToken token = default);

    [Get(ApiRoutes.Devices)]
    Task<List<DeviceDto>> GetDevicesAsync(CancellationToken token = default);

    [Post(ApiRoutes.DeviceAction)]
    Task<OperationResultDto> RunDeviceActionAsync(int deviceId, int actionId, CancellationToken token = default);

    [Get(ApiRoutes.Workflows)]
    Task<List<WorkflowDto>> GetWorkflowsAsync(CancellationToken token = default);

    [Post(ApiRoutes.WorkflowRun)]
    Task<OperationResultDto> RunWorkflowAsync(int workflowId, CancellationToken token = default);

    [Put(ApiRoutes.WorkflowEnabled)]
    Task<OperationResultDto> SetWorkflowEnabledAsync(int workflowId, [Body] SetEnabledRequest request, CancellationToken token = default);

    [Get(ApiRoutes.Cameras)]
    Task<List<CameraDto>> GetCamerasAsync(CancellationToken token = default);

    [Get(ApiRoutes.CameraSnapshot)]
    Task<Stream> GetCameraSnapshotAsync(string camera, int? height, CancellationToken token = default);
}

public interface ICameraStreamApi
{
    [Get(ApiRoutes.CameraStream)]
    Task<Stream> GetCameraStreamAsync(string camera, int? height, int? fps, CancellationToken token = default);
}
