using Microsoft.AspNetCore.Http;
using SharpClaw.Gateway.Abstractions;
using SharpClaw.Modules.Transcription.DTOs;

namespace SharpClaw.Modules.Transcription.Gateway;

/// <summary>
/// Public gateway projection of the Transcription module's REST query
/// surface, mounted under <c>/api/modules/transcription/jobs</c>. Mirrors
/// the <c>WebAccessGatewayExtension</c> pattern: reads forward through
/// <see cref="IGatewayInternalApi"/>. There are no mutations on the
/// transcription job query surface — job lifecycle (submit, approve,
/// cancel, pause, resume) flows through the core agent-job endpoints,
/// which already have their own classic gateway projection. Live
/// streaming (WebSocket / SSE) rides <c>TranscriptionStreamingProxy</c>
/// in the gateway and is gated by its own toggle, so this extension
/// covers REST polling only.
/// </summary>
public sealed class TranscriptionGatewayExtension : IGatewayModuleExtension
{
    /// <inheritdoc />
    public string ModuleId => "transcription";

    /// <inheritdoc />
    public string DisplayName => "Transcription";

    /// <inheritdoc />
    public IReadOnlyList<GatewayEndpointGroup> GetEndpointGroups() =>
    [
        new GatewayEndpointGroup(
            GroupId: "jobs",
            DisplayName: "Transcription jobs",
            Description: "Read-only access to transcription job records and segment polling.",
            RateLimitPolicy: null, // null → global policy (60/min sliding)
            DefaultEnabled: false),
    ];

    /// <inheritdoc />
    public void MapEndpoints(IGatewayEndpointGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // ── Reads forward directly through the internal API ──────
        builder.MapGet("/", async (
                IGatewayInternalApi api,
                CancellationToken ct,
                Guid? inputAudioId = null) =>
        {
            var path = inputAudioId is { } id
                ? $"/transcription/jobs?inputAudioId={id}"
                : "/transcription/jobs";
            return Results.Ok(await api.GetAsync<IReadOnlyList<TranscriptionJobResponse>>(path, ct));
        });

        builder.MapGet("/summaries", async (
                IGatewayInternalApi api,
                CancellationToken ct,
                Guid? inputAudioId = null) =>
        {
            var path = inputAudioId is { } id
                ? $"/transcription/jobs/summaries?inputAudioId={id}"
                : "/transcription/jobs/summaries";
            return Results.Ok(
                await api.GetAsync<IReadOnlyList<TranscriptionJobSummaryResponse>>(path, ct));
        });

        builder.MapGet("/{jobId:guid}", async (
                Guid jobId, IGatewayInternalApi api, CancellationToken ct) =>
            await api.GetAsync<TranscriptionJobResponse>($"/transcription/jobs/{jobId}", ct)
                is { } found
                    ? Results.Ok(found)
                    : Results.NotFound());

        builder.MapGet("/{jobId:guid}/segments", async (
                Guid jobId,
                IGatewayInternalApi api,
                CancellationToken ct,
                DateTimeOffset? since = null) =>
        {
            var path = since is { } s
                ? $"/transcription/jobs/{jobId}/segments?since={Uri.EscapeDataString(s.ToString("O"))}"
                : $"/transcription/jobs/{jobId}/segments";
            return await api.GetAsync<IReadOnlyList<TranscriptionSegmentResponse>>(path, ct)
                is { } found
                    ? Results.Ok(found)
                    : Results.NotFound();
        });
    }
}
