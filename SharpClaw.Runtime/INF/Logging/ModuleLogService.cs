using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.DTOs.Diagnostics;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Shared.DurableStorage;

namespace SharpClaw.Runtime.INF.Logging;

/// <summary>
/// Bounded asynchronous adapter that writes module diagnostics to one durable
/// stream per module and backend boot. The in-memory counters are health hints,
/// not a second canonical copy of log content.
/// </summary>
public sealed class ModuleLogService : IAsyncDisposable
{
    internal const string CategoryPrefix = "SharpClaw.Modules.";
    private readonly ExecutionDiagnosticStore _diagnostics;
    private readonly Channel<ModuleWriteCommand> _queue;
    private readonly ConcurrentDictionary<string, DiagnosticCounts> _counts =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _dropped =
        new(StringComparer.Ordinal);
    private readonly Task _processor;
    private int _queueDepth;
    private int _disposeState;

    public ModuleLogService(
        ExecutionDiagnosticStore diagnostics,
        Guid? bootId = null,
        int queueCapacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (queueCapacity < 16)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        _diagnostics = diagnostics;
        BootId = bootId ?? Guid.NewGuid();
        _queue = Channel.CreateBounded<ModuleWriteCommand>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        _processor = Task.Run(ProcessAsync);
    }

    public Guid BootId { get; }
    public int QueueDepth => Volatile.Read(ref _queueDepth);

    public void Append(
        string moduleId,
        LogLevel level,
        string message,
        Exception? exception)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(message);
        var detail = exception is null
            ? message
            : message + Environment.NewLine + exception;
        var command = new ModuleWriteCommand(
            moduleId,
            level,
            detail,
            exception?.GetType().FullName);
        if (_queue.Writer.TryWrite(command))
        {
            Interlocked.Increment(ref _queueDepth);
            return;
        }

        if (level < LogLevel.Error)
        {
            _dropped.AddOrUpdate(moduleId, 1, static (_, count) => count + 1);
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            _queue.Writer.WriteAsync(command, timeout.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            Interlocked.Increment(ref _queueDepth);
        }
        catch (OperationCanceledException)
        {
            _dropped.AddOrUpdate(moduleId, 1, static (_, count) => count + 1);
        }
    }

    public ValueTask<DurableLogPageResponse> ReadAsync(
        string moduleId,
        Guid bootId,
        string? cursor,
        DurableLogQuery query,
        CancellationToken cancellationToken = default) =>
        _diagnostics.ReadModuleLogsAsync(
            moduleId,
            bootId,
            cursor,
            query,
            cancellationToken);

    public (int Errors, int Warnings) GetDiagnosticCounts(string moduleId)
    {
        return _counts.TryGetValue(moduleId, out var counts)
            ? (counts.Errors, counts.Warnings)
            : (0, 0);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;
        _queue.Writer.TryComplete();
        await _processor.ConfigureAwait(false);
    }

    private async Task ProcessAsync()
    {
        await foreach (var command in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _queueDepth);
            if (_dropped.TryRemove(command.ModuleId, out var dropped) && dropped > 0)
            {
                await _diagnostics.AppendModuleLogAsync(
                    command.ModuleId,
                    BootId,
                    $"{dropped} module diagnostic records were dropped because the queue was full.",
                    "Warning",
                    "RecordsDropped",
                    writeMode: DurableWriteMode.Durable).ConfigureAwait(false);
            }

            await _diagnostics.AppendModuleLogAsync(
                command.ModuleId,
                BootId,
                command.Message,
                command.Level.ToString(),
                "ModuleDiagnostic",
                command.ExceptionType,
                writeMode: command.Level >= LogLevel.Error
                    ? DurableWriteMode.Durable
                    : DurableWriteMode.Buffered).ConfigureAwait(false);

            var counts = _counts.GetOrAdd(command.ModuleId, static _ => new DiagnosticCounts());
            if (command.Level >= LogLevel.Error)
                Interlocked.Increment(ref counts.Errors);
            else if (command.Level == LogLevel.Warning)
                Interlocked.Increment(ref counts.Warnings);
        }
    }

    private sealed record ModuleWriteCommand(
        string ModuleId,
        LogLevel Level,
        string Message,
        string? ExceptionType);

    private sealed class DiagnosticCounts
    {
        public int Errors;
        public int Warnings;
    }
}
