using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

public class ScheduledTaskDB : BaseEntity
{
    public required string Name { get; set; }
    public required DateTimeOffset NextRunAt { get; set; }
    public TimeSpan? RepeatInterval { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryCount { get; set; }
    public ScheduledTaskStatus Status { get; set; } = ScheduledTaskStatus.Pending;
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

public enum ScheduledTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
