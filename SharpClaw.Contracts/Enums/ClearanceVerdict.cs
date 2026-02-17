namespace SharpClaw.Contracts.Enums;

/// <summary>
/// The outcome of a permission / clearance check for an agent action.
/// </summary>
public enum ClearanceVerdict
{
    /// <summary>The agent does not hold the required permission at all.</summary>
    Denied,

    /// <summary>
    /// The agent holds the permission but the caller does not satisfy the
    /// required clearance level. The action is paused until an authorised
    /// user or agent approves it.
    /// </summary>
    PendingApproval,

    /// <summary>
    /// The agent holds the permission and the caller satisfies the required
    /// clearance. The action may proceed immediately.
    /// </summary>
    Approved
}
