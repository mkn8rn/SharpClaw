using Microsoft.AspNetCore.Http;
using SharpClaw.Runtime.Host.Routing;
using SharpClaw.Runtime.BLL.Services;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Runtime.INF.DurableStorage;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Runtime.Host.Handlers;

[RouteGroup("/channels/{channelId:guid}/jobs")]
public static class AgentJobHandlers
{
    [MapPost]
    public static async Task<IResult> Submit(
        Guid channelId, SubmitAgentJobRequest request, AgentJobService svc, ChatService chatSvc)
    {
        var job = await svc.SubmitAsync(channelId, request);
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapGet]
    public static async Task<IResult> List(
        Guid channelId,
        AgentJobService svc,
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default) =>
        Results.Ok(await svc.ListSummariesAsync(channelId, cursor, take, ct));

    [MapGet("/{jobId:guid}")]
    public static async Task<IResult> GetById(
        Guid channelId, Guid jobId, AgentJobService svc, ChatService chatSvc)
    {
        var job = await GetScopedJobAsync(channelId, jobId, svc);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapPost("/{jobId:guid}/approve")]
    public static async Task<IResult> Approve(
        Guid channelId, Guid jobId, ApproveAgentJobRequest request, AgentJobService svc, ChatService chatSvc)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null) return Results.NotFound();
        var job = await svc.ApproveAsync(jobId, request);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapPost("/{jobId:guid}/stop")]
    public static async Task<IResult> Stop(
        Guid channelId, Guid jobId, AgentJobService svc, ChatService chatSvc)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null) return Results.NotFound();
        var job = await svc.StopAsync(jobId);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapPost("/{jobId:guid}/cancel")]
    public static async Task<IResult> Cancel(
        Guid channelId, Guid jobId, AgentJobService svc, ChatService chatSvc)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null) return Results.NotFound();
        var job = await svc.CancelAsync(jobId);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapPut("/{jobId:guid}/pause")]
    public static async Task<IResult> Pause(
        Guid channelId, Guid jobId, AgentJobService svc, ChatService chatSvc)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null) return Results.NotFound();
        var job = await svc.PauseAsync(jobId);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapPut("/{jobId:guid}/resume")]
    public static async Task<IResult> Resume(
        Guid channelId, Guid jobId, AgentJobService svc, ChatService chatSvc)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null) return Results.NotFound();
        var job = await svc.ResumeAsync(jobId);
        if (job is null) return Results.NotFound();
        var cost = await chatSvc.GetChannelCostAsync(job.ChannelId);
        return Results.Ok(job with { ChannelCost = cost });
    }

    [MapGet("/{jobId:guid}/logs")]
    public static async Task<IResult> GetLogs(
        Guid channelId,
        Guid jobId,
        AgentJobService svc,
        string? cursor = null,
        int take = 200,
        int maxBytes = 262_144,
        string? minimumLevel = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? contains = null,
        long maxScanBytes = 16 * 1024 * 1024,
        CancellationToken ct = default)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null)
            return Results.NotFound();

        var page = await svc.ReadLogsAsync(
            jobId,
            cursor,
            new DurableLogQuery(
                take,
                maxBytes,
                minimumLevel,
                from,
                to,
                contains,
                maxScanBytes),
            ct);
        return Results.Ok(page);
    }

    [MapGet("/{jobId:guid}/audit")]
    public static async Task<IResult> GetAudit(
        Guid channelId,
        Guid jobId,
        AgentJobService svc,
        string? cursor = null,
        int take = 50,
        CancellationToken ct = default)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null)
            return Results.NotFound();
        return Results.Ok(await svc.ReadAuditAsync(jobId, cursor, take, ct));
    }

    [MapGet("/{jobId:guid}/artifacts/{artifactId:guid}")]
    public static async Task<IResult> GetArtifact(
        Guid channelId,
        Guid jobId,
        Guid artifactId,
        AgentJobService svc,
        IExecutionArtifactStore artifacts,
        HttpContext context,
        CancellationToken ct)
    {
        if (await GetScopedJobSummaryAsync(channelId, jobId, svc) is null)
            return Results.NotFound();

        var handle = await artifacts.OpenReadAsync(
            artifactId,
            ExecutionOwnerKind.AgentJob,
            jobId,
            cancellationToken: ct);
        if (handle is null)
            return Results.NotFound();

        context.Response.RegisterForDisposeAsync(handle);
        context.Response.Headers.ETag = $"\"{handle.Descriptor.Sha256}\"";
        return Results.Stream(
            handle.Content,
            handle.Descriptor.MediaType,
            enableRangeProcessing: true);
    }

    private static async Task<AgentJobDetailResponse?> GetScopedJobAsync(
        Guid channelId, Guid jobId, AgentJobService svc)
    {
        var job = await svc.GetAsync(jobId);
        return job?.ChannelId == channelId ? job : null;
    }

    private static async Task<AgentJobSummaryResponse?> GetScopedJobSummaryAsync(
        Guid channelId, Guid jobId, AgentJobService svc)
    {
        var job = await svc.GetSummaryAsync(jobId);
        return job?.ChannelId == channelId ? job : null;
    }
}
