namespace HomeManagement.Application;

public enum OperationStatus
{
    Succeeded,

    /// <summary>
    /// Something the hub depends on, e.g. a device or Telegram, failed.
    /// </summary>
    Failed,

    NotFound,

    /// <summary>
    /// It cannot run now, e.g. the device is offline or has not reported a value yet.
    /// </summary>
    Unavailable
}

public sealed record OperationResult(OperationStatus Status, string Message)
{
    public bool IsSuccess => Status == OperationStatus.Succeeded;

    public static OperationResult Success(string message) => new(OperationStatus.Succeeded, message);

    public static OperationResult Failure(string message) => new(OperationStatus.Failed, message);

    public static OperationResult NotFound(string message) => new(OperationStatus.NotFound, message);

    public static OperationResult Unavailable(string message) => new(OperationStatus.Unavailable, message);
}
