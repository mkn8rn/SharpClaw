using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Agents;

namespace SharpClaw.Application.API.Handlers;

[RouteGroup("/agents")]
public static class AgentHandlers
{
    [MapPost]
    public static async Task<IResult> Create(CreateAgentRequest request, AgentService svc)
        => Results.Ok(await svc.CreateAsync(request));

    [MapGet]
    public static async Task<IResult> List(AgentService svc)
        => Results.Ok(await svc.ListAsync());

    [MapGet("/{id:guid}")]
    public static async Task<IResult> GetById(Guid id, AgentService svc)
    {
        var agent = await svc.GetByIdAsync(id);
        return agent is not null ? Results.Ok(agent) : Results.NotFound();
    }

    [MapPut("/{id:guid}")]
    public static async Task<IResult> Update(Guid id, UpdateAgentRequest request, AgentService svc)
    {
        var agent = await svc.UpdateAsync(id, request);
        return agent is not null ? Results.Ok(agent) : Results.NotFound();
    }

    [MapDelete("/{id:guid}")]
    public static async Task<IResult> Delete(Guid id, AgentService svc)
        => await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    [MapPut("/{id:guid}/role")]
    public static async Task<IResult> AssignRole(
        Guid id, AssignAgentRoleRequest request, AgentService svc)
    {
        try
        {
            var agent = await svc.AssignRoleAsync(id, request.RoleId, request.CallerUserId);
            return agent is not null ? Results.Ok(agent) : Results.NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
