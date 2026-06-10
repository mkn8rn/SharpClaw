using System.Text.Json;

namespace SharpClaw.Contracts.Modules;

public interface IModuleStorageGateway
{
    IReadOnlyList<ModuleStorageContractDescriptor> ListContracts();

    Task<JsonElement> InvokeAsync(
        string moduleId,
        string storageName,
        string operation,
        JsonElement parameters,
        CancellationToken ct = default);
}

public sealed record ModuleStorageContractDescriptor(
    string ModuleId,
    string StorageName,
    IReadOnlyList<ModuleStorageOperationDescriptor> Operations,
    string? Description = null);

public sealed record ModuleStorageOperationDescriptor(
    string Name,
    string? Description = null);

public sealed class ModuleDocumentStore<T>(
    IModuleStorageGateway gateway,
    string moduleId,
    string storageName,
    JsonSerializerOptions? jsonOptions = null)
{
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<T?> GetAsync(string key, CancellationToken ct = default)
    {
        using var parameters = JsonDocument.Parse(
            JsonSerializer.Serialize(new { key }, _jsonOptions));
        var response = await gateway.InvokeAsync(
            moduleId,
            storageName,
            "get",
            parameters.RootElement,
            ct);

        if (!response.TryGetProperty("found", out var found)
            || found.ValueKind != JsonValueKind.True
            || !response.TryGetProperty("value", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        return value.Deserialize<T>(_jsonOptions);
    }

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
    {
        using var parameters = JsonDocument.Parse("{}");
        var response = await gateway.InvokeAsync(
            moduleId,
            storageName,
            "list",
            parameters.RootElement,
            ct);

        return DeserializeRecords(response);
    }

    public async Task<IReadOnlyList<T>> QueryAsync(
        string indexName,
        object? equals = null,
        object? lessThanOrEqual = null,
        object? greaterThanOrEqual = null,
        string order = "asc",
        int? limit = null,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["indexName"] = indexName,
            ["order"] = order,
        };
        if (equals is not null) payload["equals"] = equals;
        if (lessThanOrEqual is not null) payload["lessThanOrEqual"] = lessThanOrEqual;
        if (greaterThanOrEqual is not null) payload["greaterThanOrEqual"] = greaterThanOrEqual;
        if (limit is not null) payload["limit"] = limit;

        using var parameters = JsonDocument.Parse(JsonSerializer.Serialize(payload, _jsonOptions));
        var response = await gateway.InvokeAsync(
            moduleId,
            storageName,
            "query",
            parameters.RootElement,
            ct);

        return DeserializeRecords(response);
    }

    public async Task UpsertAsync(
        string key,
        T value,
        object? indexes = null,
        CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["value"] = value,
        };
        if (indexes is not null) payload["indexes"] = indexes;

        using var parameters = JsonDocument.Parse(JsonSerializer.Serialize(payload, _jsonOptions));
        await gateway.InvokeAsync(
            moduleId,
            storageName,
            "upsert",
            parameters.RootElement,
            ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        using var parameters = JsonDocument.Parse(
            JsonSerializer.Serialize(new { key }, _jsonOptions));
        var response = await gateway.InvokeAsync(
            moduleId,
            storageName,
            "delete",
            parameters.RootElement,
            ct);

        return response.TryGetProperty("deleted", out var deleted)
               && deleted.ValueKind == JsonValueKind.True;
    }

    private IReadOnlyList<T> DeserializeRecords(JsonElement response)
    {
        if (!response.TryGetProperty("records", out var records)
            || records.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<T>();
        foreach (var record in records.EnumerateArray())
        {
            if (!record.TryGetProperty("value", out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (value.Deserialize<T>(_jsonOptions) is { } item)
                result.Add(item);
        }

        return result;
    }
}
