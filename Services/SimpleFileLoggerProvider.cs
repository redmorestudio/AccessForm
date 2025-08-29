using Microsoft.Extensions.Logging;
using System;
using System.IO;

public class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _lock = new object();

    public SimpleFileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        
        // Ensure the directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SimpleFileLogger(categoryName, _filePath, _lock);
    }

    public void Dispose()
    {
    }
}

public class SimpleFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _filePath;
    private readonly object _lock;

    public SimpleFileLogger(string categoryName, string filePath, object lockObj)
    {
        _categoryName = categoryName;
        _filePath = filePath;
        _lock = lockObj;
    }

    public IDisposable BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (formatter == null) return;

        var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] [{_categoryName}] {formatter(state, exception)}";
        
        if (exception != null)
        {
            logMessage += Environment.NewLine + exception.ToString();
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_filePath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Swallow any file write errors to avoid breaking the application
            }
        }
    }
}