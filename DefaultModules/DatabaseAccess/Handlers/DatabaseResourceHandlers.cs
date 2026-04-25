using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Modules.DatabaseAccess.Dtos;
using SharpClaw.Modules.DatabaseAccess.Services;

namespace SharpClaw.Modules.DatabaseAccess.Handlers;

/// <summary>
/// Internal and external database resource CRUD endpoints under
/// <c>/resources/internaldatabases</c> and <c>/resources/externaldatabases</c>.
/// </summary>
public static class DatabaseResourceHandlers
{
    internal static void MapDatabaseResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var internalGroup = routes.MapGroup("/resources/internaldatabases");

        internalGroup.MapPost("/", CreateInternalDatabase);
        internalGroup.MapGet("/", ListInternalDatabases);
        internalGroup.MapGet("/{id:guid}", GetInternalDatabase);
        internalGroup.MapPut("/{id:guid}", UpdateInternalDatabase);
        internalGroup.MapDelete("/{id:guid}", DeleteInternalDatabase);

        var externalGroup = routes.MapGroup("/resources/externaldatabases");

        externalGroup.MapPost("/", CreateExternalDatabase);
        externalGroup.MapGet("/", ListExternalDatabases);
        externalGroup.MapGet("/{id:guid}", GetExternalDatabase);
        externalGroup.MapPut("/{id:guid}", UpdateExternalDatabase);
        externalGroup.MapDelete("/{id:guid}", DeleteExternalDatabase);
    }

    // ── Internal Databases ───────────────────────────────────────

    public static async Task<IResult> CreateInternalDatabase(
        CreateInternalDatabaseRequest request, DatabaseResourceService svc)
        => Results.Ok(await svc.CreateInternalAsync(request));

    public static async Task<IResult> ListInternalDatabases(DatabaseResourceService svc)
        => Results.Ok(await svc.ListInternalAsync());

    public static async Task<IResult> GetInternalDatabase(Guid id, DatabaseResourceService svc)
    {
        var item = await svc.GetInternalByIdAsync(id);
        return item is not null ? Results.Ok(item) : Results.NotFound();
    }

    public static async Task<IResult> UpdateInternalDatabase(
        Guid id, UpdateInternalDatabaseRequest request, DatabaseResourceService svc)
    {
        var item = await svc.UpdateInternalAsync(id, request);
        return item is not null ? Results.Ok(item) : Results.NotFound();
    }

    public static async Task<IResult> DeleteInternalDatabase(Guid id, DatabaseResourceService svc)
        => await svc.DeleteInternalAsync(id) ? Results.NoContent() : Results.NotFound();

    // ── External Databases ───────────────────────────────────────

    public static async Task<IResult> CreateExternalDatabase(
        CreateExternalDatabaseRequest request, DatabaseResourceService svc)
        => Results.Ok(await svc.CreateExternalAsync(request));

    public static async Task<IResult> ListExternalDatabases(DatabaseResourceService svc)
        => Results.Ok(await svc.ListExternalAsync());

    public static async Task<IResult> GetExternalDatabase(Guid id, DatabaseResourceService svc)
    {
        var item = await svc.GetExternalByIdAsync(id);
        return item is not null ? Results.Ok(item) : Results.NotFound();
    }

    public static async Task<IResult> UpdateExternalDatabase(
        Guid id, UpdateExternalDatabaseRequest request, DatabaseResourceService svc)
    {
        var item = await svc.UpdateExternalAsync(id, request);
        return item is not null ? Results.Ok(item) : Results.NotFound();
    }

    public static async Task<IResult> DeleteExternalDatabase(Guid id, DatabaseResourceService svc)
        => await svc.DeleteExternalAsync(id) ? Results.NoContent() : Results.NotFound();
}
