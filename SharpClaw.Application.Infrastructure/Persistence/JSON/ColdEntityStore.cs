using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Persistence;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Disk-backed read path for cold entity types that are not loaded into the
/// InMemory EF store at startup. Reads individual JSON files on demand,
/// transparently handling AES-256-GCM encryption via <see cref="JsonFileEncryption"/>.
/// <para>
/// Writes still flow through EF + <see cref="JsonFilePersistenceService"/>;
/// this class is read-only.
/// </para>
/// </summary>
public sealed class ColdEntityStore(
    JsonFileOptions options,
    EncryptionOptions encryptionOptions,
    ILogger<ColdEntityStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter(), new NullableGuidConverter() }
    };

    /// <summary>
    /// Loads a single cold entity by its primary key.
    /// Returns <c>null</c> when the file does not exist on disk.
    /// </summary>
    public async Task<T?> FindAsync<T>(Guid id, CancellationToken ct = default)
        where T : BaseEntity
    {
        var path = Path.Combine(options.DataDirectory, typeof(T).Name, $"{id}.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await JsonFileEncryption.ReadJsonAsync(path, encryptionOptions.Key, ct);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read cold entity {Type} {Id}", typeof(T).Name, id);
            return null;
        }
    }

    /// <summary>
    /// Scans all files for the given entity type, deserializes each, and
    /// returns those matching <paramref name="predicate"/>, ordered by
    /// <see cref="BaseEntity.CreatedAt"/> descending, limited to
    /// <paramref name="limit"/> results, then re-sorted chronologically.
    /// </summary>
    /// <remarks>
    /// This is O(N) over all files in the entity directory. For high-volume
    /// types (e.g. ChatMessageDB), on-disk index files should be added in
    /// a follow-up to avoid scanning every file.
    /// </remarks>
    public async Task<List<T>> QueryAsync<T>(
        Func<T, bool> predicate,
        int limit,
        CancellationToken ct = default) where T : BaseEntity
    {
        var dir = Path.Combine(options.DataDirectory, typeof(T).Name);
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
            return [];

        var readTasks = files.Select(async file =>
        {
            try
            {
                var json = await JsonFileEncryption.ReadJsonAsync(file, encryptionOptions.Key, ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read cold entity from {Path}", file);
                return null;
            }
        });

        var entities = await Task.WhenAll(readTasks);
        return entities
            .Where(e => e is not null && predicate(e))
            .OrderByDescending(e => e!.CreatedAt)
            .Take(limit)
            .OrderBy(e => e!.CreatedAt)
            .ToList()!;
    }

    /// <summary>
    /// Variant of <see cref="QueryAsync{T}"/> that returns all matching
    /// entities without a limit, ordered chronologically.
    /// </summary>
    public async Task<List<T>> QueryAllAsync<T>(
        Func<T, bool> predicate,
        CancellationToken ct = default) where T : BaseEntity
    {
        var dir = Path.Combine(options.DataDirectory, typeof(T).Name);
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.GetFiles(dir, "*.json");
        if (files.Length == 0)
            return [];

        var readTasks = files.Select(async file =>
        {
            try
            {
                var json = await JsonFileEncryption.ReadJsonAsync(file, encryptionOptions.Key, ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read cold entity from {Path}", file);
                return null;
            }
        });

        var entities = await Task.WhenAll(readTasks);
        return entities
            .Where(e => e is not null && predicate(e))
            .OrderBy(e => e!.CreatedAt)
            .ToList()!;
    }

    /// <summary>
    /// Handles JSON <c>null</c> for non-nullable <see cref="Guid"/> properties
    /// by substituting <see cref="Guid.Empty"/>.
    /// </summary>
    private sealed class NullableGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? Guid.Empty : reader.GetGuid();

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
