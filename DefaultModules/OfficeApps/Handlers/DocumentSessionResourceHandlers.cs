using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Contracts.DTOs.Documents;
using SharpClaw.Modules.OfficeApps.Services;

namespace SharpClaw.Modules.OfficeApps.Handlers;

/// <summary>
/// Document session resource CRUD endpoints under <c>/resources/documents</c>.
/// </summary>
public static class DocumentSessionResourceHandlers
{
    internal static void MapDocumentSessionResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/resources/documents");

        group.MapPost("/", CreateDocumentSession);
        group.MapGet("/", ListDocumentSessions);
        group.MapGet("/{id}", GetDocumentSession);
        group.MapPut("/{id}", UpdateDocumentSession);
        group.MapDelete("/{id}", DeleteDocumentSession);
    }

    public static async Task<IResult> CreateDocumentSession(
        CreateDocumentSessionRequest request, DocumentSessionService svc)
        => Results.Ok(await svc.CreateAsync(request));

    public static async Task<IResult> ListDocumentSessions(DocumentSessionService svc)
        => Results.Ok(await svc.ListAsync());

    public static async Task<IResult> GetDocumentSession(Guid id, DocumentSessionService svc)
        => await svc.GetByIdAsync(id) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> UpdateDocumentSession(
        Guid id, UpdateDocumentSessionRequest request, DocumentSessionService svc)
        => await svc.UpdateAsync(id, request) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> DeleteDocumentSession(Guid id, DocumentSessionService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
}
