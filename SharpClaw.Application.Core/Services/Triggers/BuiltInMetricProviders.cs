using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Tasks;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Core.Services.Triggers;

/// <summary>
/// Reports the number of pending (Queued) agent jobs (<c>Queue.PendingJobCount</c>).
/// </summary>
public sealed class PendingJobCountMetricProvider(IServiceProvider services) : ITaskMetricProvider
{
    public string MetricName  => "Queue.PendingJobCount";
    public string Description => "Number of agent jobs currently in the Queued state.";

    public async Task<double> GetValueAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
        var count = await db.AgentJobs
            .CountAsync(j => j.Status == AgentJobStatus.Queued, ct);
        return count;
    }
}

/// <summary>
/// Reports the number of pending (Queued) task instances (<c>Queue.PendingTaskCount</c>).
/// </summary>
public sealed class PendingTaskCountMetricProvider(IServiceProvider services) : ITaskMetricProvider
{
    public string MetricName  => "Queue.PendingTaskCount";
    public string Description => "Number of task instances currently in the Queued state.";

    public async Task<double> GetValueAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
        var count = await db.TaskInstances
            .CountAsync(i => i.Status == TaskInstanceStatus.Queued, ct);
        return count;
    }
}

/// <summary>
/// Reports the number of scheduled jobs with a past <c>NextRunAt</c>
/// that have not yet run (<c>Scheduler.PendingJobCount</c>).
/// </summary>
public sealed class SchedulerPendingJobCountMetricProvider(IServiceProvider services) : ITaskMetricProvider
{
    public string MetricName  => "Scheduler.PendingJobCount";
    public string Description => "Number of scheduled jobs past their NextRunAt time that have not been triggered.";

    public async Task<double> GetValueAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<SharpClawDbContext>();
        var now   = DateTimeOffset.UtcNow;
        var count = await db.ScheduledTasks
            .CountAsync(j => j.Status == ScheduledTaskStatus.Pending && j.NextRunAt <= now, ct);
        return count;
    }
}
