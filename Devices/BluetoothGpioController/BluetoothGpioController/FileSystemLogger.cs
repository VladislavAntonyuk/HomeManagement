namespace BluetoothGpioController;

public class FileSystemLogger(string categoryName, string filePath) : ILogger
{
    private static readonly Lock Lock = new();

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
    
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {categoryName}: {formatter(state, exception)}";

        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        lock (Lock)
        {
            File.AppendAllText(filePath, message + Environment.NewLine);
        }
    }
}