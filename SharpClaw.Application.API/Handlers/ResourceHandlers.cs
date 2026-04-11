using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.API.Routing;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.API.Handlers;

/// <summary>
/// Universal resource lookup endpoint.  Type-specific CRUD endpoints
/// are registered by their owning modules via <see cref="SharpClaw.Contracts.Modules.ISharpClawModule.MapEndpoints"/>.
/// </summary>
[RouteGroup("/resources")]
public static class ResourceHandlers
{
    // ═══════════════════════════════════════════════════════════════
    // Universal resource lookup
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns lightweight <c>[{id, name}]</c> items for the resource type
    /// that backs a given permission access category.  The <paramref name="type"/>
    /// value matches the <see cref="ResourceAccessDB.ResourceType"/> discriminator
    /// (e.g. <c>TrAudio</c>, <c>Container</c>).
    /// </summary>
    [MapGet("/lookup/{type}")]
    public static async Task<IResult> LookupByAccessType(string type, SharpClawDbContext db)
    {
        IQueryable<ResourceItem>? query = type switch
        {
            "DsShell" => db.SystemUsers
                .Select(e => new ResourceItem(e.Id, e.Username)),
            "Mk8Shell" or "Container" => db.Containers
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "WaWebsite" => db.Websites
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "WaSearch" => db.SearchEngines
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "DbInternal" => db.InternalDatabases
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "DbExternal" => db.ExternalDatabases
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "TrAudio" => db.InputAudios
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "CuDisplay" => db.DisplayDevices
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "EditorSession" => db.EditorSessions
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AoAgent" => db.Agents
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AoTask" => db.ScheduledTasks
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AoSkill" => db.Skills
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AoAgentHeader" => db.Agents
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AoChannelHeader" => db.Channels
                .Select(e => new ResourceItem(e.Id, e.Title ?? e.Id.ToString())),
            "OaDocument" => db.DocumentSessions
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "CuNativeApp" => db.NativeApplications
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "BiChannel" => db.BotIntegrations
                .Select(e => new ResourceItem(e.Id, e.Name)),
            _ => null,
        };

        if (query is null)
            return Results.BadRequest(new { error = $"Unknown resource type '{type}'." });

        return Results.Ok(await query.OrderBy(r => r.Name).ToListAsync());
    }

    private sealed record ResourceItem(Guid Id, string Name);
}
