using System.Collections.Concurrent;

namespace BluetoothGpioController;

public class FileSystemLoggerProvider(string filePath) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileSystemLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileSystemLogger(name, filePath));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}