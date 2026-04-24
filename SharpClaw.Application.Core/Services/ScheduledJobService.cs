using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Contracts.DTOs.Tasks;
using SharpClaw.Contracts.Enums;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

/// <summary>
/// CRUD and lifecycle management for <see cref="ScheduledJobDB"/> entities.
/// Cron validation is enforced on create and update.
/// </summary>
public sealed class ScheduledJobService(
    SharpClawDbContext db,
    ILogger<ScheduledJobService> logger)
{
    // ═══════════════════════════════════════════════════════════════
    // Validation constants
    // ═══════════════════════════════════════════════════════════════

    public const string ErrBothSchedules  = "SCHED001";
    public const string ErrInvalidCron    = "SCHED002";
    public const string ErrInvalidTz      = "SCHED003";
    public const string WarnNeverFires    = "SCHED004";

    // ═══════════════════════════════════════════════════════════════
    // CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validate cron fields and persist a new scheduled job.
    /// Throws <see cref="InvalidOperationException"/> on hard validation errors.
    /// </summary>
    public async Task<ScheduledJobResponse> CreateAsync(
        CreateScheduledJobRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (nextRunAt, warnings) = ValidateCronFields(
            request.CronExpression,
            request.CronTimezone,
            request.RepeatInterval,
            request.NextRunAt);

        foreach (var w in warnings)
            logger.LogWarning("Scheduled job '{Name}': {Warning}", request.Name, w);

        string? paramJson = request.ParameterValues is { Count: > 0 }
            ? JsonSerializer.Serialize(request.ParameterValues)
            : null;

        var entity = new ScheduledJobDB
        {
            Name                = request.Name,
            TaskDefinitionId    = request.TaskDefinitionId,
            NextRunAt           = nextRunAt,
            RepeatInterval      = request.RepeatInterval,
            CronExpression      = request.CronExpression,
            CronTimezone        = request.CronTimezone,
            MissedFirePolicy    = request.MissedFirePolicy,
            ParameterValuesJson = paramJson,
            CallerAgentId       = request.CallerAgentId,
            MaxRetries          = request.MaxRetries,
        };

        db.ScheduledTasks.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<ScheduledJobResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<IReadOnlyList<ScheduledJobResponse>> ListAsync(
        CancellationToken ct = default)
        => await db.ScheduledTasks
            .OrderBy(j => j.NextRunAt)
            .Select(j => ToResponse(j))
            .ToListAsync(ct);

    /// <summary>
    /// Partial update. Validates cron fields if expression or timezone changes.
    /// </summary>
    public async Task<ScheduledJobResponse?> UpdateAsync(
        Guid id, UpdateScheduledJobRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        if (entity is null) return null;

        // Determine effective schedule values after potential update
        var effectiveCron     = request.CronExpression  ?? entity.CronExpression;
        var effectiveTz       = request.CronTimezone    ?? entity.CronTimezone;
        var effectiveInterval = request.RepeatInterval  ?? entity.RepeatInterval;
        var effectiveNext     = request.NextRunAt       ?? entity.NextRunAt;

        if (request.CronExpression is not null || request.CronTimezone is not null ||
            request.RepeatInterval is not null || request.NextRunAt is not null)
        {
            var (nextRunAt, warnings) = ValidateCronFields(
                effectiveCron, effectiveTz, effectiveInterval, effectiveNext);

            foreach (var w in warnings)
                logger.LogWarning("Scheduled job '{Name}' update: {Warning}", entity.Name, w);

            entity.NextRunAt = nextRunAt;
        }

        if (request.Name         is not null) entity.Name              = request.Name;
        if (request.RepeatInterval.HasValue)  entity.RepeatInterval    = request.RepeatInterval;
        if (request.CronExpression is not null) entity.CronExpression  = request.CronExpression;
        if (request.CronTimezone is not null) entity.CronTimezone      = request.CronTimezone;
        if (request.MissedFirePolicy.HasValue) entity.MissedFirePolicy = request.MissedFirePolicy.Value;
        if (request.MaxRetries.HasValue)      entity.MaxRetries        = request.MaxRetries.Value;
        if (request.CallerAgentId.HasValue)   entity.CallerAgentId     = request.CallerAgentId;
        if (request.ParameterValues is not null)
            entity.ParameterValuesJson = request.ParameterValues.Count > 0
                ? JsonSerializer.Serialize(request.ParameterValues)
                : null;

        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        if (entity is null) return false;

        db.ScheduledTasks.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // Pause / Resume
    // ═══════════════════════════════════════════════════════════════

    public async Task<ScheduledJobResponse?> PauseAsync(
        Guid id, CancellationToken ct = default)
    {
        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        if (entity is null) return null;

        if (entity.Status == ScheduledTaskStatus.Pending)
        {
            entity.Status = ScheduledTaskStatus.Paused;
            await db.SaveChangesAsync(ct);
        }

        return ToResponse(entity);
    }

    public async Task<ScheduledJobResponse?> ResumeAsync(
        Guid id, CancellationToken ct = default)
    {
        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        if (entity is null) return null;

        if (entity.Status == ScheduledTaskStatus.Paused)
        {
            entity.Status = ScheduledTaskStatus.Pending;

            // Re-derive NextRunAt if the schedule has passed while paused
            if (!string.IsNullOrEmpty(entity.CronExpression))
            {
                var next = CronEvaluator.GetNextOccurrence(
                    entity.CronExpression,
                    DateTimeOffset.UtcNow,
                    entity.CronTimezone);

                if (next.HasValue)
                    entity.NextRunAt = next.Value;
                else
                    entity.Status = ScheduledTaskStatus.Completed;
            }
            else if (entity.RepeatInterval.HasValue &&
                     entity.NextRunAt < DateTimeOffset.UtcNow)
            {
                entity.NextRunAt = DateTimeOffset.UtcNow.Add(entity.RepeatInterval.Value);
            }

            await db.SaveChangesAsync(ct);
        }

        return ToResponse(entity);
    }

    // ═══════════════════════════════════════════════════════════════
    // Preview (stateless)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Compute upcoming occurrences for a stored job's cron expression.
    /// Returns null when the job does not exist or has no cron expression.
    /// </summary>
    public async Task<CronPreviewResponse?> PreviewJobAsync(
        Guid id, int count = 10, CancellationToken ct = default)
    {
        var entity = await db.ScheduledTasks.FindAsync([id], ct);
        if (entity is null || string.IsNullOrEmpty(entity.CronExpression))
            return null;

        var occurrences = CronEvaluator
            .GetNextOccurrences(entity.CronExpression, DateTimeOffset.UtcNow,
                entity.CronTimezone, count)
            .ToList();

        return new CronPreviewResponse(entity.CronExpression, entity.CronTimezone, occurrences);
    }

    /// <summary>
    /// Stateless preview — no DB access.
    /// Throws <see cref="InvalidOperationException"/> for invalid expression or timezone.
    /// </summary>
    public static CronPreviewResponse PreviewExpression(
        string expression, string? timezone = null, int count = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        if (!CronEvaluator.TryParse(expression, out var err))
            throw new InvalidOperationException($"{ErrInvalidCron}: {err}");

        if (!string.IsNullOrWhiteSpace(timezone))
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(timezone); }
            catch (TimeZoneNotFoundException)
            {
                throw new InvalidOperationException(
                    $"{ErrInvalidTz}: Unknown timezone '{timezone}'.");
            }
        }

        var occurrences = CronEvaluator
            .GetNextOccurrences(expression, DateTimeOffset.UtcNow, timezone, count)
            .ToList();

        return new CronPreviewResponse(expression, timezone, occurrences);
    }

    // ═══════════════════════════════════════════════════════════════
    // Validation helper
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates cron-related fields and returns the effective <c>NextRunAt</c>
    /// together with any non-fatal warning messages.
    /// Throws <see cref="InvalidOperationException"/> on hard errors.
    /// </summary>
    internal static (DateTimeOffset nextRunAt, IReadOnlyList<string> warnings)
        ValidateCronFields(
            string? cronExpression,
            string? cronTimezone,
            TimeSpan? repeatInterval,
            DateTimeOffset? suppliedNextRunAt)
    {
        var warnings = new List<string>();

        // SCHED001: both schedules set
        if (!string.IsNullOrEmpty(cronExpression) && repeatInterval.HasValue)
            throw new InvalidOperationException(
                $"{ErrBothSchedules}: CronExpression and RepeatInterval are mutually exclusive.");

        if (!string.IsNullOrEmpty(cronExpression))
        {
            // SCHED002: invalid cron
            if (!CronEvaluator.TryParse(cronExpression, out var cronErr))
                throw new InvalidOperationException(
                    $"{ErrInvalidCron}: {cronErr}");

            // SCHED003: invalid timezone
            if (!string.IsNullOrWhiteSpace(cronTimezone))
            {
                try { TimeZoneInfo.FindSystemTimeZoneById(cronTimezone); }
                catch (TimeZoneNotFoundException)
                {
                    throw new InvalidOperationException(
                        $"{ErrInvalidTz}: Unknown timezone '{cronTimezone}'.");
                }
            }

            // Auto-derive NextRunAt when not supplied
            var next = CronEvaluator.GetNextOccurrence(
                cronExpression, DateTimeOffset.UtcNow, cronTimezone);

            if (next is null)
            {
                // SCHED004: expression never fires — warn, allow creation
                warnings.Add(
                    $"{WarnNeverFires}: Expression '{cronExpression}' has no future " +
                    "occurrences from now. The job will never fire unless updated.");

                return (suppliedNextRunAt ?? DateTimeOffset.UtcNow, warnings);
            }

            return (suppliedNextRunAt ?? next.Value, warnings);
        }

        // Interval or one-shot
        var effective = suppliedNextRunAt ?? DateTimeOffset.UtcNow;
        return (effective, warnings);
    }

    // ═══════════════════════════════════════════════════════════════
    // Projection
    // ═══════════════════════════════════════════════════════════════

    internal static ScheduledJobResponse ToResponse(ScheduledJobDB e) => new(
        e.Id, e.Name, e.Status, e.NextRunAt,
        e.RepeatInterval, e.CronExpression, e.CronTimezone, e.MissedFirePolicy,
        e.TaskDefinitionId, e.LastRunAt, e.LastError,
        e.RetryCount, e.MaxRetries, e.CreatedAt, e.UpdatedAt);
}
