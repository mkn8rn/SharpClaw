namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Convenience predicates for module-managed job lifecycle code.
/// </summary>
public static class AgentJobStatusExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> while module work should continue.
    /// </summary>
    public static bool IsActive(this AgentJobStatus status) =>
        status is AgentJobStatus.Executing or AgentJobStatus.Paused;

    /// <summary>
    /// Returns <see langword="true"/> once the host will no longer execute,
    /// resume, or stop the job.
    /// </summary>
    public static bool IsTerminal(this AgentJobStatus status) =>
        status is AgentJobStatus.Completed
            or AgentJobStatus.Failed
            or AgentJobStatus.Denied
            or AgentJobStatus.Cancelled;
}
