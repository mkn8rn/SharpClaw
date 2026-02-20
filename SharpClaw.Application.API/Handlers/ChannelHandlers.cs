using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Conversations;

namespace SharpClaw.Application.API.Handlers;

[RouteGroup("/channels")]
public static class ChannelHandlers
{
    [MapPost]
    public static async Task<IResult> Create(CreateConversationRequest request, ConversationService svc)
        => Results.Ok(await svc.CreateAsync(request));

    [MapGet]
    public static async Task<IResult> List(ConversationService svc, Guid? agentId = null)
        => Results.Ok(await svc.ListAsync(agentId));

    [MapGet("/{id:guid}")]
    public static async Task<IResult> GetById(Guid id, ConversationService svc)
    {
        var channel = await svc.GetByIdAsync(id);
        return channel is not null ? Results.Ok(channel) : Results.NotFound();
    }

    [MapPut("/{id:guid}")]
    public static async Task<IResult> Update(Guid id, UpdateConversationRequest request, ConversationService svc)
    {
        var channel = await svc.UpdateAsync(id, request);
        return channel is not null ? Results.Ok(channel) : Results.NotFound();
    }

    [MapDelete("/{id:guid}")]
    public static async Task<IResult> Delete(Guid id, ConversationService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
}
