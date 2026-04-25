using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Modules.ComputerUse.Dtos;
using SharpClaw.Modules.ComputerUse.Services;

namespace SharpClaw.Modules.ComputerUse.Handlers;

/// <summary>
/// Display device resource CRUD endpoints under <c>/resources/displaydevices</c>.
/// </summary>
public static class DisplayDeviceResourceHandlers
{
    internal static void MapDisplayDeviceResourceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/resources/displaydevices");

        group.MapPost("/", CreateDisplayDevice);
        group.MapGet("/", ListDisplayDevices);
        group.MapGet("/{id:guid}", GetDisplayDevice);
        group.MapPut("/{id:guid}", UpdateDisplayDevice);
        group.MapDelete("/{id:guid}", DeleteDisplayDevice);
        group.MapPost("/sync", SyncDisplayDevices);
    }

    public static async Task<IResult> CreateDisplayDevice(
        CreateDisplayDeviceRequest request, DisplayDeviceService svc)
        => Results.Ok(await svc.CreateAsync(request));

    public static async Task<IResult> ListDisplayDevices(DisplayDeviceService svc)
        => Results.Ok(await svc.ListAsync());

    public static async Task<IResult> GetDisplayDevice(Guid id, DisplayDeviceService svc)
    {
        var device = await svc.GetByIdAsync(id);
        return device is not null ? Results.Ok(device) : Results.NotFound();
    }

    public static async Task<IResult> UpdateDisplayDevice(
        Guid id, UpdateDisplayDeviceRequest request, DisplayDeviceService svc)
    {
        var device = await svc.UpdateAsync(id, request);
        return device is not null ? Results.Ok(device) : Results.NotFound();
    }

    public static async Task<IResult> DeleteDisplayDevice(Guid id, DisplayDeviceService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    public static async Task<IResult> SyncDisplayDevices(DisplayDeviceService svc)
        => Results.Ok(await svc.SyncAsync());
}
