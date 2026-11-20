namespace HomeManagement.Application.Router;

public interface IRouterController
{
    Task<OperationResult> RebootAsync(CancellationToken token = default);

    Task<OperationResult> RebootModemAsync(CancellationToken token = default);

    Task<OperationResult> SetLedsAsync(bool enabled, CancellationToken token = default);
}
