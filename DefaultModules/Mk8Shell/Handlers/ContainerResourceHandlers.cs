using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Contracts.DTOs.Containers;
using SharpClaw.Modules.Mk8Shell.Services;

namespace SharpClaw.Modules.Mk8Shell.Handlers;

/// <summary>
/// Container resource CRUD endpoints under <c>/resources/containers</c>.
/// </summary>
public static class ContainerResourceHandlers
{
    internal static void MapContainerResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/resources/containers");

        group.MapPost("/", CreateContainer);
        group.MapGet("/", ListContainers);
        group.MapGet("/{id:guid}", GetContainer);
        group.MapPut("/{id:guid}", UpdateContainer);
        group.MapDelete("/{id:guid}", DeleteContainer);
        group.MapPost("/sync", SyncContainers);
    }

    public static async Task<IResult> CreateContainer(
        CreateContainerRequest request, ContainerService svc)
        => Results.Ok(await svc.CreateAsync(request));

    public static async Task<IResult> ListContainers(ContainerService svc)
        => Results.Ok(await svc.ListAsync());

    public static async Task<IResult> GetContainer(Guid id, ContainerService svc)
    {
        var container = await svc.GetByIdAsync(id);
        return container is not null ? Results.Ok(container) : Results.NotFound();
    }

    public static async Task<IResult> UpdateContainer(
        Guid id, UpdateContainerRequest request, ContainerService svc)
    {
        var container = await svc.UpdateAsync(id, request);
        return container is not null ? Results.Ok(container) : Results.NotFound();
    }

    public static async Task<IResult> DeleteContainer(Guid id, ContainerService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    public static async Task<IResult> SyncContainers(ContainerService svc)
        => Results.Ok(await svc.SyncLocalMk8ShellAsync());
}
