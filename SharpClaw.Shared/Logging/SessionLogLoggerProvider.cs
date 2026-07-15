using Microsoft.Extensions.Logging;

namespace SharpClaw.Shared.Logging;

/// <summary>
/// Routes <see cref="ILogger"/> messages into a <see cref="DurableProcessLogWriter"/>.
/// </summary>
public sealed class DurableProcessLogLoggerProvider(DurableProcessLogWriter writer) : ILoggerProvider
{
    /// <summary>
    /// Creates a logger for the specified category.
    /// </summary>
    public ILogger CreateLogger(string categoryName) =>
        new DurableProcessLogger(writer, categoryName);

    public void Dispose()
    {
    }

    private sealed class DurableProcessLogger(DurableProcessLogWriter writer, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var line = $"[{logLevel}] {categoryName}: {message}";

            if (logLevel <= LogLevel.Debug)
                writer.AppendDebug(line);
            else
                writer.AppendLog(line);

            if (exception is not null)
                writer.AppendException(exception, line);
        }
    }
}
