using Microsoft.Extensions.Logging;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Infrastructure.Configuration;

namespace WindowsServerManagement.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly ApplicationPaths _paths;
    private readonly IAppSettingsService _settings;
    private readonly object _sync = new();

    public FileLoggerProvider(ApplicationPaths paths, IAppSettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private bool IsEnabled(LogLevel logLevel)
    {
        var settings = _settings.Current;
        if (!settings.EnableFileLogging)
        {
            return false;
        }

        return Enum.TryParse<LogLevel>(settings.MinimumLogLevel, ignoreCase: true, out var minimum)
            ? logLevel >= minimum
            : logLevel >= LogLevel.Information;
    }

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_paths.LogsDirectory);
                var path = Path.Combine(_paths.LogsDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                var eventText = eventId.Id == 0 ? string.Empty : $" [{eventId.Id}]";
                var line = $"{DateTimeOffset.Now:O} [{level}] {category}{eventText}: {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(path, line + Environment.NewLine);
                DeleteOldLogs();
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(_paths.LogsDirectory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort retention.
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None && _provider.IsEnabled(logLevel);
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

            _provider.Write(_category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
