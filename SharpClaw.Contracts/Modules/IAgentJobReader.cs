using SharpClaw.Contracts.DTOs.AgentActions;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side read-only view of agent jobs.
/// Implemented by <c>AgentJobService</c>; injected into modules that
/// need to query job state without referencing Core or Infrastructure.
/// </summary>
public interface IAgentJobReader
{
    /// <summary>
    /// Returns the full job response for <paramref name="jobId"/>,
    /// or <see langword="null"/> if it does not exist.
    /// </summary>
    Task<AgentJobResponse?> GetJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns all jobs whose <c>ActionKey</c> starts with
    /// <paramref name="actionKeyPrefix"/>, optionally filtered by
    /// <c>ResourceId</c>.
    /// </summary>
    Task<IReadOnlyList<AgentJobResponse>> ListJobsByActionPrefixAsync(
        string actionKeyPrefix,
        Guid? resourceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns lightweight job summaries (no segments or logs) whose
    /// <c>ActionKey</c> starts with <paramref name="actionKeyPrefix"/>,
    /// optionally filtered by <c>ResourceId</c>.
    /// </summary>
    Task<IReadOnlyList<AgentJobSummaryResponse>> ListJobSummariesByActionPrefixAsync(
        string actionKeyPrefix,
        Guid? resourceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns whether a job with <paramref name="jobId"/> exists and
    /// its <c>ActionKey</c> starts with <paramref name="actionKeyPrefix"/>.
    /// </summary>
    Task<bool> JobExistsWithActionPrefixAsync(
        Guid jobId, string actionKeyPrefix, CancellationToken ct = default);
}
