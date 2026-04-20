using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharpClaw.Utils.Security;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Append-only JSONL event log for point-in-time recovery and audit trail (Phase H).
/// <para>
/// <b>Deliverables:</b>
/// <list type="bullet">
///   <item>Append <see cref="EventLogEntry"/> per entity change after commit.</item>
///   <item>Daily JSONL file rotation (<c>_events/events_20250101.jsonl</c>).</item>
///   <item>Retention purge: files older than <see cref="JsonFileOptions.EventLogRetentionDays"/>.</item>
///   <item><b>RGAP-7:</b> Per-line encryption when <c>EncryptAtRest</c> is enabled.
///         serialize → encrypt → base64 → single line. Preserves append-only semantics.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class EventLog
{
    internal const string EventsDirectory = "_events";
    internal const string FilePrefix = "events_";
    internal const string FileExtension = ".jsonl";
    private const string DateFormat = "yyyyMMdd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IPersistenceFileSystem _fs;
    private readonly JsonFileOptions _options;
    private readonly byte[]? _encryptionKey;
    private readonly ILogger _logger;

    internal EventLog(
        IPersistenceFileSystem fs,
        JsonFileOptions options,
        byte[]? encryptionKey,
        ILogger logger)
    {
        _fs = fs;
        _options = options;
        _encryptionKey = encryptionKey;
        _logger = logger;
    }

    /// <summary>
    /// Appends events for all entity changes that occurred in one flush cycle.
    /// Called after two-phase commit succeeds.
    /// </summary>
    internal async Task AppendAsync(
        IReadOnlyList<(Type ClrType, Guid Id, EntityState State)> changes,
        CancellationToken ct)
    {
        if (!_options.EnableEventLog || changes.Count == 0)
            return;

        var eventsDir = _fs.CombinePath(_options.DataDirectory, EventsDirectory);
        _fs.CreateDirectory(eventsDir);

        var today = DateTimeOffset.UtcNow;
        var filePath = GetLogFilePath(eventsDir, today);
        var encrypt = _options.EncryptAtRest && _encryptionKey is { Length: > 0 };

        var sb = new StringBuilder();
        foreach (var (clrType, id, state) in changes)
        {
            ct.ThrowIfCancellationRequested();

            var entry = new EventLogEntry
            {
                Timestamp = today,
                EntityType = clrType.Name,
                EntityId = id,
                Action = state switch
                {
                    EntityState.Added => EventAction.Created,
                    EntityState.Modified => EventAction.Updated,
                    EntityState.Deleted => EventAction.Deleted,
                    _ => EventAction.Updated,
                },
            };

            var json = JsonSerializer.Serialize(entry, JsonOptions);

            if (encrypt)
            {
                var encrypted = ApiKeyEncryptor.EncryptBytes(
                    Encoding.UTF8.GetBytes(json), _encryptionKey!);
                sb.AppendLine(Convert.ToBase64String(encrypted));
            }
            else
            {
                sb.AppendLine(json);
            }
        }

        try
        {
            await _fs.AppendAllTextAsync(filePath, sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append {Count} event(s) to {Path}", changes.Count, filePath);
        }
    }

    /// <summary>
    /// Reads all events from a specific day's log file.
    /// </summary>
    internal async Task<List<EventLogEntry>> ReadAsync(DateTimeOffset date, CancellationToken ct)
    {
        var eventsDir = _fs.CombinePath(_options.DataDirectory, EventsDirectory);
        var filePath = GetLogFilePath(eventsDir, date);
        if (!_fs.FileExists(filePath))
            return [];

        var decrypt = _options.EncryptAtRest && _encryptionKey is { Length: > 0 };
        var entries = new List<EventLogEntry>();

        using var owned = await _fs.ReadAllBytesAsync(filePath, ct);
        var text = Encoding.UTF8.GetString(owned.Span);

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string json;
                if (decrypt && IsBase64Line(line))
                {
                    var encrypted = Convert.FromBase64String(line);
                    var plain = ApiKeyEncryptor.DecryptBytes(encrypted, _encryptionKey!);
                    json = Encoding.UTF8.GetString(plain);
                }
                else
                {
                    // Plaintext or pre-encryption era line.
                    json = line;
                }

                var entry = JsonSerializer.Deserialize<EventLogEntry>(json, JsonOptions);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable event log line in {Path}", filePath);
            }
        }

        return entries;
    }

    /// <summary>
    /// Reads all events from all log files, ordered by timestamp.
    /// </summary>
    internal async Task<List<EventLogEntry>> ReadAllAsync(CancellationToken ct)
    {
        var eventsDir = _fs.CombinePath(_options.DataDirectory, EventsDirectory);
        if (!_fs.DirectoryExists(eventsDir))
            return [];

        var all = new List<EventLogEntry>();
        var files = _fs.GetFiles(eventsDir, $"*{FileExtension}")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var date = ParseDateFromFileName(_fs.GetFileName(file));
            if (date is null) continue;
            var entries = await ReadAsync(date.Value, ct);
            all.AddRange(entries);
        }

        return all;
    }

    /// <summary>
    /// Purges event log files older than the configured retention period.
    /// </summary>
    internal void PurgeExpiredLogs()
    {
        if (_options.EventLogRetentionDays <= 0)
            return;

        var eventsDir = _fs.CombinePath(_options.DataDirectory, EventsDirectory);
        if (!_fs.DirectoryExists(eventsDir))
            return;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.EventLogRetentionDays);
        var files = _fs.GetFiles(eventsDir, $"*{FileExtension}");

        foreach (var file in files)
        {
            var date = ParseDateFromFileName(_fs.GetFileName(file));
            if (date is not null && date.Value < cutoff)
            {
                try
                {
                    _fs.DeleteFile(file);
                    _logger.LogInformation("Purged expired event log {File}", file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to purge event log {File}", file);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private string GetLogFilePath(string eventsDir, DateTimeOffset date)
        => _fs.CombinePath(eventsDir, $"{FilePrefix}{date.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)}{FileExtension}");

    private static DateTimeOffset? ParseDateFromFileName(string fileName)
    {
        // events_20250101.jsonl → 20250101
        if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(FileExtension, StringComparison.Ordinal))
            return null;

        var datePart = fileName[FilePrefix.Length..^FileExtension.Length];
        return DateTime.TryParseExact(datePart, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero)
            : null;
    }

    private static bool IsBase64Line(string line)
    {
        // Quick heuristic: base64 lines won't start with '{'.
        return line.Length > 0 && line[0] != '{';
    }
}

/// <summary>
/// A single event log entry representing an entity change.
/// </summary>
internal sealed class EventLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public EventAction Action { get; init; }
}

/// <summary>
/// The type of entity change recorded in the event log.
/// </summary>
internal enum EventAction
{
    Created,
    Updated,
    Deleted,
}
