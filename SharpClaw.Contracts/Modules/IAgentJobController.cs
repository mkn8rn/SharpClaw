using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Entities.Core.Jobs;

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
        string level = JobLogLevels.Info,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a long-running job as completed. Intended for module-managed
    /// background work whose tool entrypoint returned earlier with
    /// <see cref="ModuleJobCompletionBehavior.RemainExecuting"/>.
    /// </summary>
    Task MarkJobCompletedAsync(
        Guid jobId,
        string? resultData = null,
        string? message = null,
        CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException(
            "The host does not support module-completed jobs."));

    /// <summary>Marks a job as failed and records the exception details.</summary>
    Task MarkJobFailedAsync(
        Guid jobId,
        Exception exception,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a job as failed using a module-generated diagnostic message.
    /// </summary>
    Task MarkJobFailedAsync(
        Guid jobId,
        string message,
        string? details = null,
        CancellationToken ct = default) =>
        MarkJobFailedAsync(
            jobId,
            new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? message
                    : $"{message}{Environment.NewLine}{details}"),
            ct);

    /// <summary>
    /// Marks a long-running job as cancelled by module lifecycle handling,
    /// for example during module unload or host shutdown.
    /// </summary>
    Task MarkJobCancelledAsync(
        Guid jobId,
        string? message = null,
        CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException(
            "The host does not support module-cancelled jobs."));

    /// <summary>Cancels jobs left queued or executing for a module-owned action prefix.</summary>
    Task CancelStaleJobsByActionPrefixAsync(
        string actionKeyPrefix,
        CancellationToken ct = default);
}
