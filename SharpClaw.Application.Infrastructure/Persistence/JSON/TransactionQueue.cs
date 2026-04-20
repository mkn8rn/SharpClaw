using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Durable intent-before-execution transaction queue for JSON persistence.
/// Each <see cref="FlushChangesAsync"/> call writes a manifest to
/// <c>_transactions/pending/</c> before any entity files are written.
/// On startup, pending manifests are replayed to recover from crashes.
/// </summary>
public sealed class TransactionQueue
{
    private readonly IPersistenceFileSystem _fs;
    private readonly JsonFileOptions _options;
    private readonly ILogger<TransactionQueue> _logger;

    /// <summary>Monotonic counter persisted to <c>_transactions/_sequence</c>.</summary>
    private long _sequence;
    private readonly object _sequenceLock = new();

    private string TransactionsDir => _fs.CombinePath(_options.DataDirectory, "_transactions");
    private string PendingDir => _fs.CombinePath(TransactionsDir, "pending");
    private string FailedDir => _fs.CombinePath(TransactionsDir, "failed");
    private string SequenceFile => _fs.CombinePath(TransactionsDir, "_sequence");

    /// <summary>Maximum replay retries before moving a manifest to <c>failed/</c>.</summary>
    public int MaxRetries { get; }

    public TransactionQueue(
        IPersistenceFileSystem fs,
        JsonFileOptions options,
        ILogger<TransactionQueue> logger,
        int maxRetries = 3)
    {
        _fs = fs;
        _options = options;
        _logger = logger;
        MaxRetries = maxRetries;

        EnsureDirectories();
        _sequence = LoadSequence();
    }

    /// <summary>
    /// Writes a transaction manifest describing the intended changes.
    /// Returns the manifest path so the caller can dequeue after success.
    /// </summary>
    public async Task<string> EnqueueAsync(
        IReadOnlyList<(Type ClrType, Guid Id, EntityState State)> entityChanges,
        IReadOnlySet<string> joinTableChanges,
        CancellationToken ct = default)
    {
        var seq = NextSequence();
        var timestamp = DateTimeOffset.UtcNow;
        var fileName = $"{seq:D12}_{timestamp:yyyyMMddHHmmssfff}.json";
        var manifestPath = _fs.CombinePath(PendingDir, fileName);

        var manifest = new TransactionManifest
        {
            Sequence = seq,
            Timestamp = timestamp,
            EntityChanges = entityChanges.Select(e => new EntityChangeEntry
            {
                EntityType = e.ClrType.Name,
                Id = e.Id,
                State = e.State
            }).ToList(),
            JoinTableChanges = [.. joinTableChanges],
            RetryCount = 0
        };

        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
        await _fs.WriteAllTextAsync(manifestPath, json, ct);
        _logger.LogDebug("Enqueued transaction manifest {Path} (seq {Sequence})", manifestPath, seq);

        return manifestPath;
    }

    /// <summary>
    /// Removes a completed transaction manifest from the pending directory.
    /// </summary>
    public void Dequeue(string manifestPath)
    {
        if (_fs.FileExists(manifestPath))
        {
            _fs.DeleteFile(manifestPath);
            _logger.LogDebug("Dequeued transaction manifest {Path}", manifestPath);
        }
    }

    /// <summary>
    /// Replays all pending transaction manifests in sequence order.
    /// Called at startup before <see cref="JsonFilePersistenceService.LoadAsync"/>.
    /// Returns the number of manifests replayed.
    /// </summary>
    public async Task<int> ReplayPendingAsync(
        Func<TransactionManifest, CancellationToken, Task> replayAction,
        CancellationToken ct = default)
    {
        if (!_fs.DirectoryExists(PendingDir))
            return 0;

        var files = _fs.GetFiles(PendingDir, "*.json");
        if (files.Length == 0)
            return 0;

        // Sort by filename which is {sequence:D12}_{timestamp}.json — deterministic order.
        Array.Sort(files, StringComparer.Ordinal);

        var replayed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            TransactionManifest? manifest;
            try
            {
                var json = await _fs.ReadAllTextAsync(file, ct);
                manifest = JsonSerializer.Deserialize<TransactionManifest>(json, ManifestJsonOptions);
                if (manifest is null)
                {
                    _logger.LogWarning("Empty or null manifest at {Path}, moving to failed", file);
                    MoveToFailed(file);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize manifest {Path}, moving to failed", file);
                MoveToFailed(file);
                continue;
            }

            manifest.RetryCount++;

            if (manifest.RetryCount > MaxRetries)
            {
                _logger.LogError(
                    "Transaction manifest {Path} exceeded max retries ({Max}), moving to failed",
                    file, MaxRetries);
                MoveToFailed(file);
                continue;
            }

            try
            {
                // Update retry count on disk before replaying
                var updatedJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
                await _fs.WriteAllTextAsync(file, updatedJson, ct);

                await replayAction(manifest, ct);

                // Success — remove from pending
                _fs.DeleteFile(file);
                replayed++;
                _logger.LogInformation(
                    "Replayed transaction manifest {Path} (seq {Sequence}, attempt {Attempt})",
                    file, manifest.Sequence, manifest.RetryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to replay manifest {Path} (seq {Sequence}, attempt {Attempt}/{Max})",
                    file, manifest.Sequence, manifest.RetryCount, MaxRetries);
            }
        }

        return replayed;
    }

    private void MoveToFailed(string manifestPath)
    {
        _fs.CreateDirectory(FailedDir);
        var fileName = _fs.GetFileName(manifestPath);
        var dest = _fs.CombinePath(FailedDir, fileName);
        _fs.MoveFile(manifestPath, dest);
    }

    private long NextSequence()
    {
        long seq;
        lock (_sequenceLock)
        {
            seq = ++_sequence;
        }
        PersistSequence(seq);
        return seq;
    }

    private long LoadSequence()
    {
        if (!_fs.FileExists(SequenceFile))
            return 0;

        try
        {
            // Synchronous read is acceptable for a single small file at startup.
            var text = _fs.ReadAllTextAsync(SequenceFile).GetAwaiter().GetResult();
            return long.TryParse(text.Trim(), out var val) ? val : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read transaction sequence file, resetting to 0");
            return 0;
        }
    }

    private void PersistSequence(long seq)
    {
        try
        {
            _fs.WriteAllTextAsync(SequenceFile, seq.ToString()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist transaction sequence {Sequence}", seq);
        }
    }

    private void EnsureDirectories()
    {
        _fs.CreateDirectory(TransactionsDir);
        _fs.CreateDirectory(PendingDir);
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Describes the intent of a single <see cref="SharpClawDbContext.SaveChangesAsync"/> call.
/// Persisted as JSON in <c>_transactions/pending/</c>.
/// </summary>
public sealed class TransactionManifest
{
    public long Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<EntityChangeEntry> EntityChanges { get; set; } = [];
    public List<string> JoinTableChanges { get; set; } = [];
    public int RetryCount { get; set; }
}

/// <summary>
/// A single entity change within a <see cref="TransactionManifest"/>.
/// </summary>
public sealed class EntityChangeEntry
{
    public string EntityType { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public EntityState State { get; set; }
}
