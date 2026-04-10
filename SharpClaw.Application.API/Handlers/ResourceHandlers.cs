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
    /// (e.g. <c>InputAudio</c>, <c>ContainerAccess</c>).
    /// </summary>
    [MapGet("/lookup/{type}")]
    public static async Task<IResult> LookupByAccessType(string type, SharpClawDbContext db)
    {
        IQueryable<ResourceItem>? query = type switch
        {
            "DangerousShell" => db.SystemUsers
                .Select(e => new ResourceItem(e.Id, e.Username)),
            "SafeShell" or "ContainerAccess" => db.Containers
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "WebsiteAccess" => db.Websites
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "SearchEngineAccess" => db.SearchEngines
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "InternalDatabase" => db.InternalDatabases
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "ExternalDatabase" => db.ExternalDatabases
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "InputAudio" => db.InputAudios
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "DisplayDevice" => db.DisplayDevices
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "EditorSession" => db.EditorSessions
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "ManageAgent" => db.Agents
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "EditTask" => db.ScheduledTasks
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "AccessSkill" => db.Skills
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "EditAgentHeader" => db.Agents
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "EditChannelHeader" => db.Channels
                .Select(e => new ResourceItem(e.Id, e.Title ?? e.Id.ToString())),
            "DocumentSession" => db.DocumentSessions
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "NativeApplication" => db.NativeApplications
                .Select(e => new ResourceItem(e.Id, e.Name)),
            "BotIntegration" => db.BotIntegrations
                .Select(e => new ResourceItem(e.Id, e.Name)),
            _ => null,
        };

        if (query is null)
            return Results.BadRequest(new { error = $"Unknown resource type '{type}'." });

        return Results.Ok(await query.OrderBy(r => r.Name).ToListAsync());
    }

    private sealed record ResourceItem(Guid Id, string Name);
}
