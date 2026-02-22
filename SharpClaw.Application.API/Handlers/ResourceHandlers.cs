using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Containers;
using SharpClaw.Contracts.DTOs.Transcription;

namespace SharpClaw.Application.API.Handlers;

/// <summary>
/// Unified REST surface for all resource types (containers, audio devices,
/// etc.).  Each resource type lives under <c>/resources/{type}/...</c>
/// instead of having its own top-level route group.
/// </summary>
[RouteGroup("/resources")]
public static class ResourceHandlers
{
    // ═══════════════════════════════════════════════════════════════
    // Containers
    // ═══════════════════════════════════════════════════════════════

    [MapPost("/containers")]
    public static async Task<IResult> CreateContainer(
        CreateContainerRequest request, ContainerService svc)
        => Results.Ok(await svc.CreateAsync(request));

    [MapGet("/containers")]
    public static async Task<IResult> ListContainers(ContainerService svc)
        => Results.Ok(await svc.ListAsync());

    [MapGet("/containers/{id:guid}")]
    public static async Task<IResult> GetContainer(Guid id, ContainerService svc)
    {
        var container = await svc.GetByIdAsync(id);
        return container is not null ? Results.Ok(container) : Results.NotFound();
    }

    [MapPut("/containers/{id:guid}")]
    public static async Task<IResult> UpdateContainer(
        Guid id, UpdateContainerRequest request, ContainerService svc)
    {
        var container = await svc.UpdateAsync(id, request);
        return container is not null ? Results.Ok(container) : Results.NotFound();
    }

    [MapDelete("/containers/{id:guid}")]
    public static async Task<IResult> DeleteContainer(Guid id, ContainerService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    [MapPost("/containers/sync")]
    public static async Task<IResult> SyncContainers(ContainerService svc)
        => Results.Ok(await svc.SyncLocalMk8ShellAsync());

    // ═══════════════════════════════════════════════════════════════
    // Audio Devices
    // ═══════════════════════════════════════════════════════════════

    [MapPost("/audiodevices")]
    public static async Task<IResult> CreateAudioDevice(
        CreateAudioDeviceRequest request, TranscriptionService svc)
        => Results.Ok(await svc.CreateDeviceAsync(request));

    [MapGet("/audiodevices")]
    public static async Task<IResult> ListAudioDevices(TranscriptionService svc)
        => Results.Ok(await svc.ListDevicesAsync());

    [MapGet("/audiodevices/{id:guid}")]
    public static async Task<IResult> GetAudioDevice(Guid id, TranscriptionService svc)
    {
        var device = await svc.GetDeviceByIdAsync(id);
        return device is not null ? Results.Ok(device) : Results.NotFound();
    }

    [MapPut("/audiodevices/{id:guid}")]
    public static async Task<IResult> UpdateAudioDevice(
        Guid id, UpdateAudioDeviceRequest request, TranscriptionService svc)
    {
        var device = await svc.UpdateDeviceAsync(id, request);
        return device is not null ? Results.Ok(device) : Results.NotFound();
    }

    [MapDelete("/audiodevices/{id:guid}")]
    public static async Task<IResult> DeleteAudioDevice(Guid id, TranscriptionService svc)
        => await svc.DeleteDeviceAsync(id) ? Results.NoContent() : Results.NotFound();

    [MapPost("/audiodevices/sync")]
    public static async Task<IResult> SyncAudioDevices(TranscriptionService svc)
        => Results.Ok(await svc.SyncDevicesAsync());
}
