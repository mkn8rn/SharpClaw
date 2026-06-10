using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Modules;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.API;

public sealed class BundledModuleStorageGateway(SharpClawDbContext db) : IModuleStorageGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IReadOnlyList<ModuleStorageOperationDescriptor> Operations =
    [
        new("get", "Reads one module-owned record by key."),
        new("upsert", "Writes one module-owned record and replaces its index entries."),
        new("delete", "Deletes one module-owned record and its index entries."),
        new("list", "Lists module-owned records in key order."),
        new("query", "Finds module-owned records through typed relational index entries."),
    ];

    public IReadOnlyList<ModuleStorageContractDescriptor> ListContracts() =>
    [
        new(
            "*",
            "*",
            Operations,
            "Parent-owned module document storage backed by relational records and typed index rows.")
    ];

    public async Task<JsonElement> InvokeAsync(
        string moduleId,
        string storageName,
        string operation,
        JsonElement parameters,
        CancellationToken ct = default)
    {
        moduleId = RequireIdentifier(moduleId, nameof(moduleId), 128);
        storageName = RequireIdentifier(storageName, nameof(storageName), 128);
        operation = RequireIdentifier(operation, nameof(operation), 64).ToLowerInvariant();

        return operation switch
        {
            "get" => await GetAsync(moduleId, storageName, parameters, ct),
            "upsert" => await UpsertAsync(moduleId, storageName, parameters, ct),
            "delete" => await DeleteAsync(moduleId, storageName, parameters, ct),
            "list" => await ListAsync(moduleId, storageName, parameters, ct),
            "query" => await QueryAsync(moduleId, storageName, parameters, ct),
            _ => throw new NotSupportedException(
                $"Module storage operation '{operation}' is not supported for '{moduleId}/{storageName}'."),
        };
    }

    private async Task<JsonElement> GetAsync(
        string moduleId,
        string storageName,
        JsonElement parameters,
        CancellationToken ct)
    {
        var key = ReadRequiredString(parameters, "key", 256);
        var record = await Records(moduleId, storageName)
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.RecordKey == key, ct);

        if (record is null)
            return JsonSerializer.SerializeToElement(new { found = false }, JsonOptions);

        using var value = JsonDocument.Parse(record.ValueJson);
        return JsonSerializer.SerializeToElement(new
        {
            found = true,
            key = record.RecordKey,
            value = value.RootElement,
        }, JsonOptions);
    }

    private async Task<JsonElement> UpsertAsync(
        string moduleId,
        string storageName,
        JsonElement parameters,
        CancellationToken ct)
    {
        var key = ReadRequiredString(parameters, "key", 256);
        if (!parameters.TryGetProperty("value", out var value)
            || value.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException("Module storage upsert requires a value.", nameof(parameters));
        }

        var record = await Records(moduleId, storageName)
            .SingleOrDefaultAsync(record => record.RecordKey == key, ct);
        if (record is null)
        {
            record = new ModuleStorageRecordDB
            {
                Id = Guid.NewGuid(),
                ModuleId = moduleId,
                StorageName = storageName,
                RecordKey = key,
                ValueJson = value.GetRawText(),
            };
            db.ModuleStorageRecords.Add(record);
        }
        else
        {
            record.ValueJson = value.GetRawText();
        }

        await DeleteIndexesAsync(moduleId, storageName, key, ct);
        if (parameters.TryGetProperty("indexes", out var indexes)
            && indexes.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            db.ModuleStorageIndexEntries.AddRange(ReadIndexes(moduleId, storageName, key, indexes));
        }

        await db.SaveChangesAsync(ct);
        return JsonSerializer.SerializeToElement(new { saved = true }, JsonOptions);
    }

    private async Task<JsonElement> DeleteAsync(
        string moduleId,
        string storageName,
        JsonElement parameters,
        CancellationToken ct)
    {
        var key = ReadRequiredString(parameters, "key", 256);
        var record = await Records(moduleId, storageName)
            .SingleOrDefaultAsync(record => record.RecordKey == key, ct);
        var deleted = record is not null;

        if (record is not null)
            db.ModuleStorageRecords.Remove(record);

        var removedIndexes = await DeleteIndexesAsync(moduleId, storageName, key, ct);
        if (deleted || removedIndexes)
            await db.SaveChangesAsync(ct);

        return JsonSerializer.SerializeToElement(new { deleted }, JsonOptions);
    }

    private async Task<JsonElement> ListAsync(
        string moduleId,
        string storageName,
        JsonElement parameters,
        CancellationToken ct)
    {
        var offset = ReadOptionalInt(parameters, "offset", 0, 100_000) ?? 0;
        var limit = ReadOptionalInt(parameters, "limit", 1, 1_000);
        IQueryable<ModuleStorageRecordDB> query = Records(moduleId, storageName)
            .AsNoTracking()
            .OrderBy(record => record.RecordKey)
            .Skip(offset);
        if (limit is { } take)
            query = query.Take(take);

        return RecordsResponse(await query.ToListAsync(ct));
    }

    private async Task<JsonElement> QueryAsync(
        string moduleId,
        string storageName,
        JsonElement parameters,
        CancellationToken ct)
    {
        var indexName = ReadRequiredString(parameters, "indexName", 128);
        var order = ReadOptionalString(parameters, "order", 16) ?? "asc";
        var limit = ReadOptionalInt(parameters, "limit", 1, 1_000);
        var indexQuery = db.ModuleStorageIndexEntries
            .AsNoTracking()
            .Where(index =>
                index.ModuleId == moduleId
                && index.StorageName == storageName
                && index.IndexName == indexName);

        indexQuery = ApplyComparison(indexQuery, parameters, "equals", ComparisonKind.Equals);
        indexQuery = ApplyComparison(indexQuery, parameters, "lessThanOrEqual", ComparisonKind.LessThanOrEqual);
        indexQuery = ApplyComparison(indexQuery, parameters, "greaterThanOrEqual", ComparisonKind.GreaterThanOrEqual);

        var descending = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
        var indexes = await OrderIndexes(indexQuery, descending)
            .Take(limit ?? 1_000)
            .ToListAsync(ct);
        if (indexes.Count == 0)
            return RecordsResponse([]);

        var keys = indexes.Select(index => index.RecordKey).Distinct(StringComparer.Ordinal).ToList();
        var records = await Records(moduleId, storageName)
            .AsNoTracking()
            .Where(record => keys.Contains(record.RecordKey))
            .ToListAsync(ct);
        var byKey = records.ToDictionary(record => record.RecordKey, StringComparer.Ordinal);
        var ordered = new List<ModuleStorageRecordDB>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in indexes)
        {
            if (seen.Add(index.RecordKey) && byKey.TryGetValue(index.RecordKey, out var record))
                ordered.Add(record);
        }

        return RecordsResponse(ordered);
    }

    private IQueryable<ModuleStorageRecordDB> Records(string moduleId, string storageName) =>
        db.ModuleStorageRecords.Where(record =>
            record.ModuleId == moduleId
            && record.StorageName == storageName);

    private static IQueryable<ModuleStorageIndexEntryDB> ApplyComparison(
        IQueryable<ModuleStorageIndexEntryDB> query,
        JsonElement parameters,
        string propertyName,
        ComparisonKind comparison)
    {
        if (!parameters.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return query;
        }

        var typed = ReadIndexValue(value);
        return typed.Kind switch
        {
            IndexValueKind.String => comparison switch
            {
                ComparisonKind.Equals => query.Where(index => index.StringValue == typed.StringValue),
                _ => throw new ArgumentException("String index values only support equality comparisons.", nameof(parameters)),
            },
            IndexValueKind.Number => comparison switch
            {
                ComparisonKind.Equals => query.Where(index => index.NumberValue == typed.NumberValue),
                ComparisonKind.LessThanOrEqual => query.Where(index => index.NumberValue <= typed.NumberValue),
                ComparisonKind.GreaterThanOrEqual => query.Where(index => index.NumberValue >= typed.NumberValue),
                _ => query,
            },
            IndexValueKind.DateTime => comparison switch
            {
                ComparisonKind.Equals => query.Where(index => index.DateTimeValue == typed.DateTimeValue),
                ComparisonKind.LessThanOrEqual => query.Where(index => index.DateTimeValue <= typed.DateTimeValue),
                ComparisonKind.GreaterThanOrEqual => query.Where(index => index.DateTimeValue >= typed.DateTimeValue),
                _ => query,
            },
            IndexValueKind.Bool => comparison switch
            {
                ComparisonKind.Equals => query.Where(index => index.BoolValue == typed.BoolValue),
                _ => throw new ArgumentException("Boolean index values only support equality comparisons.", nameof(parameters)),
            },
            _ => query,
        };
    }

    private static IOrderedQueryable<ModuleStorageIndexEntryDB> OrderIndexes(
        IQueryable<ModuleStorageIndexEntryDB> query,
        bool descending) =>
        descending
            ? query
                .OrderByDescending(index => index.DateTimeValue)
                .ThenByDescending(index => index.NumberValue)
                .ThenByDescending(index => index.StringValue)
                .ThenByDescending(index => index.BoolValue)
                .ThenByDescending(index => index.RecordKey)
            : query
                .OrderBy(index => index.DateTimeValue)
                .ThenBy(index => index.NumberValue)
                .ThenBy(index => index.StringValue)
                .ThenBy(index => index.BoolValue)
                .ThenBy(index => index.RecordKey);

    private async Task<bool> DeleteIndexesAsync(
        string moduleId,
        string storageName,
        string key,
        CancellationToken ct)
    {
        var indexes = await db.ModuleStorageIndexEntries
            .Where(index =>
                index.ModuleId == moduleId
                && index.StorageName == storageName
                && index.RecordKey == key)
            .ToListAsync(ct);
        db.ModuleStorageIndexEntries.RemoveRange(indexes);
        return indexes.Count > 0;
    }

    private static IEnumerable<ModuleStorageIndexEntryDB> ReadIndexes(
        string moduleId,
        string storageName,
        string key,
        JsonElement indexes)
    {
        if (indexes.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Module storage indexes must be a JSON object.", nameof(indexes));

        foreach (var property in indexes.EnumerateObject())
        {
            var indexName = RequireIdentifier(property.Name, "indexName", 128);
            foreach (var value in ExpandIndexValues(property.Value))
            {
                var typed = ReadIndexValue(value);
                var entry = new ModuleStorageIndexEntryDB
                {
                    Id = Guid.NewGuid(),
                    ModuleId = moduleId,
                    StorageName = storageName,
                    IndexName = indexName,
                    RecordKey = key,
                };

                switch (typed.Kind)
                {
                    case IndexValueKind.String:
                        entry.StringValue = typed.StringValue;
                        break;
                    case IndexValueKind.Number:
                        entry.NumberValue = typed.NumberValue;
                        break;
                    case IndexValueKind.DateTime:
                        entry.DateTimeValue = typed.DateTimeValue;
                        break;
                    case IndexValueKind.Bool:
                        entry.BoolValue = typed.BoolValue;
                        break;
                }

                yield return entry;
            }
        }
    }

    private static IEnumerable<JsonElement> ExpandIndexValues(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];

        if (value.ValueKind != JsonValueKind.Array)
            return [value];

        return value.EnumerateArray()
            .Where(item => item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            .ToArray();
    }

    private static IndexValue ReadIndexValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            return DateTimeOffset.TryParse(text, out var dateTime)
                ? new IndexValue(IndexValueKind.DateTime, null, null, dateTime, null)
                : new IndexValue(IndexValueKind.String, text, null, null, null);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return new IndexValue(IndexValueKind.Number, null, number, null, null);

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new IndexValue(IndexValueKind.Bool, null, null, null, value.GetBoolean());

        throw new ArgumentException(
            $"Module storage indexes do not support JSON value kind '{value.ValueKind}'.",
            nameof(value));
    }

    private static JsonElement RecordsResponse(IReadOnlyList<ModuleStorageRecordDB> records)
    {
        var items = records.Select(record =>
        {
            using var value = JsonDocument.Parse(record.ValueJson);
            return new
            {
                key = record.RecordKey,
                value = value.RootElement.Clone(),
            };
        });

        return JsonSerializer.SerializeToElement(new { records = items }, JsonOptions);
    }

    private static string ReadRequiredString(JsonElement parameters, string propertyName, int maxLength)
    {
        if (!parameters.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"Module storage parameter '{propertyName}' is required.", nameof(parameters));
        }

        return RequireIdentifier(property.GetString()!, propertyName, maxLength);
    }

    private static string? ReadOptionalString(JsonElement parameters, string propertyName, int maxLength)
    {
        if (!parameters.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Module storage parameter '{propertyName}' must be a string.", nameof(parameters));

        return RequireIdentifier(property.GetString() ?? "", propertyName, maxLength);
    }

    private static int? ReadOptionalInt(
        JsonElement parameters,
        string propertyName,
        int min,
        int max)
    {
        if (!parameters.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
            throw new ArgumentException($"Module storage parameter '{propertyName}' must be an integer.", nameof(parameters));

        return Math.Clamp(value, min, max);
    }

    private static string RequireIdentifier(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Module storage '{parameterName}' is required.", parameterName);
        if (value.Length > maxLength)
            throw new ArgumentException($"Module storage '{parameterName}' cannot exceed {maxLength} characters.", parameterName);

        return value.Trim();
    }

    private enum ComparisonKind
    {
        Equals,
        LessThanOrEqual,
        GreaterThanOrEqual,
    }

    private enum IndexValueKind
    {
        String,
        Number,
        DateTime,
        Bool,
    }

    private sealed record IndexValue(
        IndexValueKind Kind,
        string? StringValue,
        double? NumberValue,
        DateTimeOffset? DateTimeValue,
        bool? BoolValue);
}
