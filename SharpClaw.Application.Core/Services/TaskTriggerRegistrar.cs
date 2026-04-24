using SharpClaw.Application.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharpClaw.Application.Infrastructure.Models.Jobs;
using SharpClaw.Application.Infrastructure.Models.Tasks;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Application.Core.Services.Triggers;

namespace SharpClaw.Application.Services;

/// <summary>
/// Translates parsed <see cref="TaskTriggerDefinition"/> entries into
/// <see cref="ScheduledJobDB"/> rows (for Cron) and
/// <see cref="TaskTriggerBindingDB"/> rows (for all other kinds), keeping
/// both tables in sync whenever a task definition is created, updated, or
/// deleted.
/// </summary>
public sealed class TaskTriggerRegistrar(
    SharpClawDbContext db,
    ILogger<TaskTriggerRegistrar> logger,
    TaskTriggerHostService? hostService = null,
    IShortcutLauncherService? shortcutLauncher = null)
{
    /// <summary>The host service that reloads trigger sources, if registered.</summary>
    public TaskTriggerHostService? HostService => hostService;
    /// <summary>
    /// Upsert all trigger bindings derived from <paramref name="triggers"/>.
    /// Stale rows (present in the DB but absent from the new list) are removed.
    /// Does not call <see cref="SharpClawDbContext.SaveChangesAsync"/> —
    /// the caller is responsible for the surrounding unit of work.
    /// After EF changes are staged this method signals the host service so it
    /// reloads active sources.
    /// </summary>
    public async Task SyncTriggersAsync(
        TaskDefinitionDB entity,
        IReadOnlyList<TaskTriggerDefinition> triggers,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(triggers);

        await SyncCronJobsAsync(entity, triggers, ct);
        await SyncBindingRowsAsync(entity, triggers, ct);
    }

    /// <summary>Remove all trigger bindings (cron jobs + binding rows) for a definition.</summary>
    public async Task RemoveTriggersAsync(Guid definitionId, CancellationToken ct = default)
    {
        await RemoveCronJobsAsync(definitionId, ct);
        await RemoveBindingRowsAsync(definitionId, ct);
    }

    // ─────────────────────────────────────────────────────────────
    // Cron: ScheduledJobDB
    // ─────────────────────────────────────────────────────────────

    private async Task SyncCronJobsAsync(
        TaskDefinitionDB entity,
        IReadOnlyList<TaskTriggerDefinition> triggers,
        CancellationToken ct)
    {
        var incomingCron = triggers
            .Where(t => t.Kind == TriggerKind.Cron && !string.IsNullOrWhiteSpace(t.CronExpression))
            .ToList();

        var existing = await db.ScheduledTasks
            .Where(j => j.TaskDefinitionId == entity.Id && j.CronExpression != null)
            .ToListAsync(ct);

        var incomingExpressions = incomingCron
            .Select(t => t.CronExpression!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove stale cron jobs
        foreach (var stale in existing.Where(j => !incomingExpressions.Contains(j.CronExpression!)))
        {
            db.ScheduledTasks.Remove(stale);
            logger.LogDebug(
                "Removed stale cron job for definition {DefinitionId}, expression '{Expression}'.",
                entity.Id, stale.CronExpression);
        }

        // Add new cron jobs
        var existingExpressions = existing
            .Select(j => j.CronExpression!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var t in incomingCron.Where(t => !existingExpressions.Contains(t.CronExpression!)))
        {
            var nextRun = CronEvaluator.GetNextOccurrence(
                t.CronExpression!,
                DateTimeOffset.UtcNow,
                t.CronTimezone);

            if (nextRun is null)
            {
                logger.LogWarning(
                    "Cron expression '{Expression}' for definition {DefinitionId} has no future occurrences; skipping.",
                    t.CronExpression, entity.Id);
                continue;
            }

            db.ScheduledTasks.Add(new ScheduledJobDB
            {
                Name             = entity.Name,
                TaskDefinitionId = entity.Id,
                CronExpression   = t.CronExpression,
                CronTimezone     = t.CronTimezone,
                NextRunAt        = nextRun.Value,
            });

            logger.LogDebug(
                "Created cron job for definition {DefinitionId}, expression '{Expression}', next at {Next}.",
                entity.Id, t.CronExpression, nextRun.Value);
        }
    }

    private async Task RemoveCronJobsAsync(Guid definitionId, CancellationToken ct)
    {
        var jobs = await db.ScheduledTasks
            .Where(j => j.TaskDefinitionId == definitionId && j.CronExpression != null)
            .ToListAsync(ct);

        db.ScheduledTasks.RemoveRange(jobs);
    }

    // ─────────────────────────────────────────────────────────────
    // Non-cron: TaskTriggerBindingDB
    // ─────────────────────────────────────────────────────────────

    private async Task SyncBindingRowsAsync(
        TaskDefinitionDB entity,
        IReadOnlyList<TaskTriggerDefinition> triggers,
        CancellationToken ct)
    {
        var nonCron = triggers
            .Where(t => t.Kind != TriggerKind.Cron)
            .ToList();

        var existing = await db.TaskTriggerBindings
            .Where(b => b.TaskDefinitionId == entity.Id)
            .ToListAsync(ct);

        // Build a lookup key for each incoming trigger
        var incomingKeys = nonCron
            .Select(t => BindingKey(entity.Id, t))
            .ToHashSet();

        // Remove stale bindings
        foreach (var stale in existing.Where(b => !incomingKeys.Contains(BindingKey(b))))
        {
            db.TaskTriggerBindings.Remove(stale);
            logger.LogDebug(
                "Removed stale trigger binding for definition {DefinitionId}, kind {Kind}.",
                entity.Id, stale.Kind);
        }

        // Upsert new bindings
        var existingKeys = existing
            .ToDictionary(b => BindingKey(b));

        foreach (var t in nonCron)
        {
            var key = BindingKey(entity.Id, t);
            if (existingKeys.ContainsKey(key))
                continue; // already present

            db.TaskTriggerBindings.Add(new TaskTriggerBindingDB
            {
                TaskDefinitionId = entity.Id,
                Kind             = t.Kind.ToString(),
                TriggerValue     = TriggerValueFor(t),
                Filter           = TriggerFilterFor(t),
                DefinitionJson   = System.Text.Json.JsonSerializer.Serialize(t),
            });

            if (t.Kind == TriggerKind.OsShortcut && shortcutLauncher is not null)
            {
                var customId = entity.Name;
                await shortcutLauncher.WriteShortcutAsync(t, customId, ct);
            }

            logger.LogDebug(
                "Created trigger binding for definition {DefinitionId}, kind {Kind}.",
                entity.Id, t.Kind);
        }
    }

    private async Task RemoveBindingRowsAsync(Guid definitionId, CancellationToken ct)
    {
        var bindings = await db.TaskTriggerBindings
            .Where(b => b.TaskDefinitionId == definitionId)
            .ToListAsync(ct);

        if (shortcutLauncher is not null)
        {
            foreach (var b in bindings.Where(b => b.Kind == TriggerKind.OsShortcut.ToString()))
            {
                var customId = b.TriggerValue ?? b.TaskDefinitionId.ToString();
                await shortcutLauncher.RemoveShortcutsAsync(customId, ct);
            }
        }

        db.TaskTriggerBindings.RemoveRange(bindings);
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static string BindingKey(Guid definitionId, TaskTriggerDefinition t) =>
        $"{definitionId}|{t.Kind}|{TriggerValueFor(t)}";

    private static string BindingKey(TaskTriggerBindingDB b) =>
        $"{b.TaskDefinitionId}|{b.Kind}|{b.TriggerValue}";

    private static string? TriggerValueFor(TaskTriggerDefinition t) => t.Kind switch
    {
        TriggerKind.Event           => t.EventType,
        TriggerKind.FileChanged     => t.WatchPath,
        TriggerKind.ProcessStarted
            or TriggerKind.ProcessStopped
            or TriggerKind.WindowFocused
            or TriggerKind.WindowBlurred => t.ProcessName,
        TriggerKind.Webhook         => t.WebhookRoute,
        TriggerKind.HostReachable
            or TriggerKind.HostUnreachable => t.HostName,
        TriggerKind.TaskCompleted
            or TriggerKind.TaskFailed => t.SourceTaskName,
        TriggerKind.Hotkey          => t.HotkeyCombo,
        TriggerKind.NetworkChanged  => t.NetworkSsid,
        TriggerKind.DeviceConnected
            or TriggerKind.DeviceDisconnected => t.DeviceClass,
        TriggerKind.QueryReturnsRows => t.SqlQuery,
        TriggerKind.MetricThreshold  => t.MetricSource,
        TriggerKind.OsShortcut       => t.ShortcutLabel,
        TriggerKind.Custom           => t.CustomSourceName,
        _ => null,
    };

    private static string? TriggerFilterFor(TaskTriggerDefinition t) => t.Kind switch
    {
        TriggerKind.Event  => t.EventFilter,
        TriggerKind.Custom => t.CustomSourceFilter,
        _                  => null,
    };
}
