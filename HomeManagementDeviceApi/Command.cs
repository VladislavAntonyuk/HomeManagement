namespace HomeManagementDeviceApi;

public record Command(string FileName)
{
    public IEnumerable<string> Arguments { get; init; } = [];
}