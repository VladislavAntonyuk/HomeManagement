namespace HomeManagement.Shared;

/// <summary>
/// Result of a command, published by the device to the response topic of the command request.
/// </summary>
public record CommandResponse(bool IsSuccess, string Output);
