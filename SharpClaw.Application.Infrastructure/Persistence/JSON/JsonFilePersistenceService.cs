using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Handles loading entities from and saving entities to JSON files on disk,
/// keeping the InMemory EFC database in sync with the file system.
/// </summary>
public sealed class JsonFilePersistenceService(
    SharpClawDbContext context,
    JsonFileOptions options,
    ILogger<JsonFilePersistenceService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    /// <summary>
    /// Loads all JSON files from the data directory into the InMemory database.
    /// Call once at startup after the DbContext is configured.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.DataDirectory);

        // Collect navigation property names per CLR type so we can null them
        // after deserialization, preventing EF from traversing the object graph
        // and hitting duplicate-key conflicts on related entities.
        var navigations = context.Model.GetEntityTypes()
            .ToDictionary(
                e => e.ClrType,
                e => e.GetNavigations()
                    .Select(n => n.PropertyInfo)
                    .Where(p => p is not null)
                    .ToList());

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var filePath = GetFilePath(clrType);

            if (!File.Exists(filePath))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                var listType = typeof(List<>).MakeGenericType(clrType);
                var entities = JsonSerializer.Deserialize(json, listType, JsonOptions);

                if (entities is not System.Collections.IEnumerable enumerable)
                    continue;

                var navProps = navigations.GetValueOrDefault(clrType);

                foreach (var entity in enumerable)
                {
                    // Clear navigation properties so EF only tracks this entity
                    // by its foreign-key columns, not the nested object graph.
                    if (navProps is not null)
                    {
                        foreach (var prop in navProps)
                            prop!.SetValue(entity, null);
                    }

                    context.Add(entity!);
                }

                logger.LogDebug("Loaded {Type} from {Path}", clrType.Name, filePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load {Type} from {Path}", clrType.Name, filePath);
            }
        }

        await context.SaveChangesAsync(ct);
        DetachAll();
    }

    /// <summary>
    /// Saves all tracked entity types from the InMemory database to JSON files.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.DataDirectory);

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var filePath = GetFilePath(clrType);

            try
            {
                var queryable = (IQueryable<object>)context
                    .GetType()
                    .GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                    .MakeGenericMethod(clrType)
                    .Invoke(context, null)!;

                var entities = await queryable.ToListAsync(ct);
                var json = JsonSerializer.Serialize(entities, entities.GetType(), JsonOptions);
                await File.WriteAllTextAsync(filePath, json, ct);

                logger.LogDebug("Flushed {Count} {Type} to {Path}", entities.Count, clrType.Name, filePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to flush {Type} to {Path}", clrType.Name, filePath);
            }
        }
    }

    private string GetFilePath(Type entityType)
        => Path.Combine(options.DataDirectory, $"{entityType.Name}.json");

    private void DetachAll()
    {
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
