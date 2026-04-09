using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Contracts.DTOs.Bots;
using SharpClaw.Contracts.Enums;
using SharpClaw.Modules.BotIntegration.Services;

namespace SharpClaw.Modules.BotIntegration.Handlers;

/// <summary>
/// Registers minimal-API REST endpoints for Bot Integration CRUD.
/// Routes are placed under <c>/bots</c> to match the original surface.
/// </summary>
public static class BotEndpoints
{
    /// <summary>
    /// Maps the Bot Integration CRUD + config endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapBotEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/bots");

        group.MapGet("/", async (BotIntegrationService svc)
            => Results.Ok(await svc.ListAsync()));

        group.MapPost("/", async (CreateBotIntegrationRequest request, BotIntegrationService svc) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Name is required." });

            var bot = await svc.CreateAsync(request);
            return Results.Created($"/bots/{bot.Id}", bot);
        });

        group.MapGet("/{id:guid}", async (Guid id, BotIntegrationService svc) =>
        {
            var bot = await svc.GetByIdAsync(id);
            return bot is not null ? Results.Ok(bot) : Results.NotFound();
        });

        group.MapGet("/type/{type}", async (string type, BotIntegrationService svc) =>
        {
            if (!Enum.TryParse<BotType>(type, ignoreCase: true, out var botType))
                return Results.BadRequest(new { error = $"Unknown bot type: {type}" });

            var bot = await svc.GetByTypeAsync(botType);
            return bot is not null ? Results.Ok(bot) : Results.NotFound();
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateBotIntegrationRequest request, BotIntegrationService svc) =>
        {
            try
            {
                var bot = await svc.UpdateAsync(id, request);
                return Results.Ok(bot);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (Guid id, BotIntegrationService svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });

        group.MapGet("/config/{type}", async (string type, BotIntegrationService svc) =>
        {
            if (!Enum.TryParse<BotType>(type, ignoreCase: true, out var botType))
                return Results.BadRequest(new { error = $"Unknown bot type: {type}" });

            var (enabled, token, defaultChannelId, defaultThreadId, platformConfig) = await svc.GetBotConfigAsync(botType);
            return Results.Ok(new { enabled, botToken = token ?? "", defaultChannelId, defaultThreadId, platformConfig });
        });

        return routes;
    }
}
