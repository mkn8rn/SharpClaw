using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Contracts.DTOs.NativeApplications;
using SharpClaw.Modules.ComputerUse.Services;

namespace SharpClaw.Modules.ComputerUse.Handlers;

/// <summary>
/// Native application resource CRUD endpoints under <c>/resources/nativeapplications</c>.
/// </summary>
public static class NativeApplicationResourceHandlers
{
    internal static void MapNativeApplicationResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/resources/nativeapplications");

        group.MapPost("/", CreateNativeApplication);
        group.MapGet("/", ListNativeApplications);
        group.MapGet("/{id}", GetNativeApplication);
        group.MapPut("/{id}", UpdateNativeApplication);
        group.MapDelete("/{id}", DeleteNativeApplication);
    }

    public static async Task<IResult> CreateNativeApplication(
        CreateNativeApplicationRequest request, NativeApplicationService svc)
        => Results.Ok(await svc.CreateAsync(request));

    public static async Task<IResult> ListNativeApplications(NativeApplicationService svc)
        => Results.Ok(await svc.ListAsync());

    public static async Task<IResult> GetNativeApplication(Guid id, NativeApplicationService svc)
        => await svc.GetByIdAsync(id) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> UpdateNativeApplication(
        Guid id, UpdateNativeApplicationRequest request, NativeApplicationService svc)
        => await svc.UpdateAsync(id, request) is { } r ? Results.Ok(r) : Results.NotFound();

    public static async Task<IResult> DeleteNativeApplication(Guid id, NativeApplicationService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
}
