using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Contracts.DTOs.Editor;
using SharpClaw.Modules.EditorCommon.Services;

namespace SharpClaw.Modules.EditorCommon.Handlers;

/// <summary>
/// Editor session resource CRUD endpoints under <c>/resources/editorsessions</c>.
/// </summary>
public static class EditorSessionResourceHandlers
{
    internal static void MapEditorSessionResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/resources/editorsessions");

        group.MapPost("/", CreateEditorSession);
        group.MapGet("/", ListEditorSessions);
        group.MapGet("/{id}", GetEditorSession);
        group.MapPut("/{id}", UpdateEditorSession);
        group.MapDelete("/{id}", DeleteEditorSession);
    }

    public static async Task<IResult> CreateEditorSession(
        CreateEditorSessionRequest request, EditorSessionService svc)
        => Results.Ok(await svc.CreateAsync(request));

    public static async Task<IResult> ListEditorSessions(EditorSessionService svc)
        => Results.Ok(await svc.ListAsync());

    public static async Task<IResult> GetEditorSession(Guid id, EditorSessionService svc)
        => await svc.GetByIdAsync(id) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> UpdateEditorSession(
        Guid id, UpdateEditorSessionRequest request, EditorSessionService svc)
        => await svc.UpdateAsync(id, request) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> DeleteEditorSession(Guid id, EditorSessionService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
}
