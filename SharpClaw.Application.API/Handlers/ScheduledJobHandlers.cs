using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Tasks;

namespace SharpClaw.Application.API.Handlers;

// ═══════════════════════════════════════════════════════════════════
// Scheduled jobs   /scheduled-jobs
// ═══════════════════════════════════════════════════════════════════

[RouteGroup("/scheduled-jobs")]
public static class ScheduledJobHandlers
{
    [MapPost]
    public static async Task<IResult> Create(
        CreateScheduledJobRequest request, ScheduledJobService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.CreateAsync(request, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
    }

    [MapGet]
    public static async Task<IResult> List(ScheduledJobService svc, CancellationToken ct)
        => Results.Ok(await svc.ListAsync(ct));

    [MapGet("/{jobId:guid}")]
    public static async Task<IResult> GetById(Guid jobId, ScheduledJobService svc,
        CancellationToken ct)
    {
        var job = await svc.GetByIdAsync(jobId, ct);
        return job is not null ? Results.Ok(job) : Results.NotFound();
    }

    [MapPut("/{jobId:guid}")]
    public static async Task<IResult> Update(
        Guid jobId, UpdateScheduledJobRequest request,
        ScheduledJobService svc, CancellationToken ct)
    {
        try
        {
            var result = await svc.UpdateAsync(jobId, request, ct);
            return result is not null ? Results.Ok(result) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
    }

    [MapDelete("/{jobId:guid}")]
    public static async Task<IResult> Delete(Guid jobId, ScheduledJobService svc,
        CancellationToken ct)
        => await svc.DeleteAsync(jobId, ct) ? Results.NoContent() : Results.NotFound();

    // ── Pause / Resume ─────────────────────────────────────────────

    [MapPost("/{jobId:guid}/pause")]
    public static async Task<IResult> Pause(Guid jobId, ScheduledJobService svc,
        CancellationToken ct)
    {
        var result = await svc.PauseAsync(jobId, ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    [MapPost("/{jobId:guid}/resume")]
    public static async Task<IResult> Resume(Guid jobId, ScheduledJobService svc,
        CancellationToken ct)
    {
        var result = await svc.ResumeAsync(jobId, ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    // ── Preview endpoints ─────────────────────────────────────────

    /// <summary>
    /// Returns the next N occurrences for an existing job's cron expression.
    /// Query: ?count=10
    /// </summary>
    [MapGet("/{jobId:guid}/preview")]
    public static async Task<IResult> PreviewJob(
        Guid jobId, int count, ScheduledJobService svc, CancellationToken ct)
    {
        count = count <= 0 ? 10 : Math.Min(count, 100);
        var result = await svc.PreviewJobAsync(jobId, count, ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    /// <summary>
    /// Stateless preview — validates and evaluates the given expression.
    /// Query: ?expression=…&amp;timezone=…&amp;count=10
    /// </summary>
    [MapGet("/preview")]
    public static IResult PreviewExpression(
        string expression, string? timezone, int count)
    {
        count = count <= 0 ? 10 : Math.Min(count, 100);
        try
        {
            var result = ScheduledJobService.PreviewExpression(expression, timezone, count);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
    }
}
