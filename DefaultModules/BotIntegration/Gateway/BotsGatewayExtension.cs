using Microsoft.AspNetCore.Http;
using SharpClaw.Gateway.Abstractions;
using SharpClaw.Modules.BotIntegration.Contracts;
using SharpClaw.Modules.BotIntegration.Dtos;

namespace SharpClaw.Modules.BotIntegration.Gateway;

/// <summary>
/// Public gateway projection of the BotIntegration REST surface, mounted
/// under <c>/api/modules/botintegration/bots</c>. Mirrors the
/// <c>WebAccessGatewayExtension</c> pattern: reads forward through
/// <see cref="IGatewayInternalApi"/>; mutations queue through
/// <see cref="IGatewayDispatcher"/>. Disabled by default — operators opt
/// in through the <c>Gateway:Modules</c> configuration block.
/// </summary>
public sealed class BotsGatewayExtension : IGatewayModuleExtension
{
    /// <inheritdoc />
    public string ModuleId => "botintegration";

    /// <inheritdoc />
    public string DisplayName => "Bot Integration";

    /// <inheritdoc />
    public IReadOnlyList<GatewayEndpointGroup> GetEndpointGroups() =>
    [
        new GatewayEndpointGroup(
            GroupId: "bots",
            DisplayName: "Bot integrations",
            Description: "Public CRUD over registered bot integrations.",
            RateLimitPolicy: null, // null → global policy (60/min sliding)
            DefaultEnabled: false),
    ];

    /// <inheritdoc />
    public void MapEndpoints(IGatewayEndpointGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // ── Reads forward directly through the internal API ──────
        builder.MapGet("/", async (IGatewayInternalApi api, CancellationToken ct) =>
            Results.Ok(await api.GetAsync<IReadOnlyList<BotIntegrationResponse>>(
                "/bots", ct)));

        builder.MapGet("/{id:guid}", async (
                Guid id, IGatewayInternalApi api, CancellationToken ct) =>
            await api.GetAsync<BotIntegrationResponse>($"/bots/{id}", ct)
                is { } found
                    ? Results.Ok(found)
                    : Results.NotFound());

        builder.MapGet("/type/{type}", async (
                string type, IGatewayInternalApi api, CancellationToken ct) =>
        {
            if (!Enum.TryParse<BotType>(type, ignoreCase: true, out _))
                return Results.BadRequest(new { error = $"Unknown bot type: {type}" });

            return await api.GetAsync<BotIntegrationResponse>($"/bots/type/{type}", ct)
                is { } found
                    ? Results.Ok(found)
                    : Results.NotFound();
        });

        // ── Mutations go through the queue dispatcher ───────────
        builder.MapPost("/", async (
                CreateBotIntegrationRequest body,
                IGatewayDispatcher dispatcher,
                CancellationToken ct) =>
            (await dispatcher.PostAsync("/bots", body, ct)).ToResult());

        builder.MapPut("/{id:guid}", async (
                Guid id,
                UpdateBotIntegrationRequest body,
                IGatewayDispatcher dispatcher,
                CancellationToken ct) =>
            (await dispatcher.PutAsync($"/bots/{id}", body, ct)).ToResult());

        builder.MapDelete("/{id:guid}", async (
                Guid id,
                IGatewayDispatcher dispatcher,
                CancellationToken ct) =>
            (await dispatcher.DeleteAsync($"/bots/{id}", ct)).ToResult());
    }
}
