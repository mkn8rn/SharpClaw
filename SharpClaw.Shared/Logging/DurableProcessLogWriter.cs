using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using SharpClaw.Shared.DurableStorage;
using SharpClaw.Shared.Instances;
using SharpClaw.Shared.Security;

namespace SharpClaw.Shared.Logging;

/// <summary>
/// Bounded process-log adapter over the common segmented durable store. Every
/// process lifetime has a distinct boot stream; startup never deletes history.
/// </summary>
public sealed partial class DurableProcessLogWriter : IAsyncDisposable, IDisposable
{
    private readonly DurableSegmentStore _store;
    private readonly bool _ownsStore;
    private readonly Channel<ProcessWriteCommand> _channel;
    private readonly CancellationTokenSource _timerCancellation = new();
    private readonly Task _processorTask;
    private readonly Task _flushTask;
    private long _droppedRecords;
    private int _disposeState;

    public DurableProcessLogWriter(
        string appName,
        DurableSegmentStore store,
        Guid? bootId = null,
        TimeSpan? flushInterval = null,
        int queueCapacity = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentNullException.ThrowIfNull(store);
        if (queueCapacity < 16)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));

        AppName = appName.Trim().ToLowerInvariant();
        BootId = bootId ?? Guid.NewGuid();
        StreamKey = DurableStreamKey.Process(AppName, BootId);
        _store = store;
        _channel = Channel.CreateBounded<ProcessWriteCommand>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        _processorTask = Task.Run(ProcessQueueAsync);
        _flushTask = Task.Run(
            () => FlushLoopAsync(flushInterval ?? TimeSpan.FromSeconds(1)));
    }

    public DurableProcessLogWriter(
        string appName,
        SharpClawInstancePaths instancePaths,
        TimeSpan? flushInterval = null,
        int queueCapacity = 4096)
        : this(
            appName,
            CreateStore(instancePaths),
            flushInterval: flushInterval,
            queueCapacity: queueCapacity)
    {
        _ownsStore = true;
    }

    public string AppName { get; }
    public Guid BootId { get; }
    public DurableStreamKey StreamKey { get; }
    public long DroppedRecords => Interlocked.Read(ref _droppedRecords);

    public void AppendLog(string message) =>
        Enqueue("Information", "ProcessLog", message, null, blockForCapacity: false);

    public void AppendDebug(string message) =>
        Enqueue("Debug", "ProcessDebug", message, null, blockForCapacity: false);

    public void AppendException(string message) =>
        Enqueue("Error", "ProcessException", message, null, blockForCapacity: true);

    public void AppendException(Exception exception, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : context + Environment.NewLine + exception;
        Enqueue(
            "Error",
            "ProcessException",
            message,
            exception.GetType().FullName,
            blockForCapacity: true);
    }

    [Conditional("DEBUG")]
    public void AppendDebugAndTrace(string message, string category)
    {
        Debug.WriteLine(message, category);
        AppendDebug($"[{category}] {message}");
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(
            new ProcessWriteCommand(null, completion),
            cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _timerCancellation.Cancel();
        _channel.Writer.TryComplete();
        try
        {
            await _flushTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        await _processorTask.ConfigureAwait(false);
        await _store.SealAsync(StreamKey).ConfigureAwait(false);
        if (_ownsStore)
            await _store.DisposeAsync().ConfigureAwait(false);
        _timerCancellation.Dispose();
    }

    private void Enqueue(
        string level,
        string eventName,
        string? message,
        string? exceptionType,
        bool blockForCapacity)
    {
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        var record = new DurableRecordWrite(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            level,
            eventName,
            Redact(message ?? string.Empty),
            exceptionType);
        var command = new ProcessWriteCommand(record, null);
        if (_channel.Writer.TryWrite(command))
            return;

        if (!blockForCapacity)
        {
            Interlocked.Increment(ref _droppedRecords);
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            _channel.Writer.WriteAsync(command, timeout.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _droppedRecords);
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var command in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (command.Record is null)
                {
                    await _store.FlushAsync(StreamKey).ConfigureAwait(false);
                    command.Completion?.SetResult();
                    continue;
                }

                var dropped = Interlocked.Exchange(ref _droppedRecords, 0);
                if (dropped > 0)
                {
                    await _store.AppendAsync(
                        StreamKey,
                        new DurableRecordWrite(
                            Guid.NewGuid(),
                            DateTimeOffset.UtcNow,
                            "Warning",
                            "RecordsDropped",
                            $"{dropped} process diagnostic records were dropped because the queue was full."),
                        DurableWriteMode.Buffered).ConfigureAwait(false);
                }

                var mode = command.Record.Level is "Error" or "Critical"
                    ? DurableWriteMode.Durable
                    : DurableWriteMode.Buffered;
                await _store.AppendAsync(StreamKey, command.Record, mode)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                command.Completion?.SetException(ex);
            }
        }

        await _store.FlushAsync(StreamKey).ConfigureAwait(false);
    }

    private async Task FlushLoopAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(_timerCancellation.Token)
                   .ConfigureAwait(false))
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (await _channel.Writer.WaitToWriteAsync(_timerCancellation.Token)
                    .ConfigureAwait(false))
            {
                await _channel.Writer.WriteAsync(
                    new ProcessWriteCommand(null, completion),
                    _timerCancellation.Token).ConfigureAwait(false);
                await completion.Task.ConfigureAwait(false);
            }
        }
    }

    private static DurableSegmentStore CreateStore(SharpClawInstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var rootKey = EncryptionKeyResolver.ResolveKey(paths)
            ?? throw new InvalidOperationException("SharpClaw instance encryption key is unavailable.");
        return new DurableSegmentStore(new DurableStorageOptions
        {
            RootDirectory = Path.Combine(paths.InstanceRoot, "durable", "v1"),
            EncryptionKey = DurableStorageKeyDerivation.Derive(rootKey, "records"),
            AcquireWriterLease = false,
        });
    }

    private static string Redact(string message)
    {
        var redacted = AuthorizationPattern().Replace(message, "$1[REDACTED]");
        return SecretPattern().Replace(redacted, "$1[REDACTED]");
    }

    [GeneratedRegex(
        "(?i)(authorization\\s*[:=]\\s*(?:bearer\\s+)?)[^\\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex(
        "(?i)((?:api[_-]?key|access[_-]?token|refresh[_-]?token|password|cookie)\\s*[:=]\\s*)[^\\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    private sealed record ProcessWriteCommand(
        DurableRecordWrite? Record,
        TaskCompletionSource? Completion);
}
