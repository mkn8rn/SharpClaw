using SharpClaw.Contracts.DTOs.AgentActions;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side job lifecycle operations exposed to modules without requiring
/// module projects to reference Core or Infrastructure.
/// </summary>
public interface IAgentJobController
{
    /// <summary>Submits a module job through the host job pipeline.</summary>
    Task<AgentJobResponse> SubmitJobAsync(
        Guid channelId,
        SubmitAgentJobRequest request,
        CancellationToken ct = default);

    /// <summary>Stops a running job, optionally restricted to an action-key prefix.</summary>
    Task<AgentJobResponse?> StopJobAsync(
        Guid jobId,
        string? requiredActionPrefix = null,
        CancellationToken ct = default);

    /// <summary>Appends a log entry to a job.</summary>
    Task AddJobLogAsync(
        Guid jobId,
        string message,
        string level = "Info",
        CancellationToken ct = default);

    /// <summary>Marks a job as failed and records the exception details.</summary>
    Task MarkJobFailedAsync(
        Guid jobId,
        Exception exception,
        CancellationToken ct = default);

    /// <summary>Cancels jobs left queued or executing for a module-owned action prefix.</summary>
    Task CancelStaleJobsByActionPrefixAsync(
        string actionKeyPrefix,
        CancellationToken ct = default);
}
