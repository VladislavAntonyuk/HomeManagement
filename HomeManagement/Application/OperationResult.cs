namespace HomeManagement.Application;

public enum OperationStatus
{
    Succeeded,
    Failed,
    NotFound
}

public sealed record OperationResult(OperationStatus Status, string Message)
{
    public bool IsSuccess => Status == OperationStatus.Succeeded;

    public static OperationResult Success(string message) => new(OperationStatus.Succeeded, message);

    public static OperationResult Failure(string message) => new(OperationStatus.Failed, message);

    public static OperationResult NotFound(string message) => new(OperationStatus.NotFound, message);
}
