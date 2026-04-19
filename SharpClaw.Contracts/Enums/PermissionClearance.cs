namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Defines the level of external approval an agent requires before it can
/// act on a granted permission.  Higher values are more restrictive about
/// <em>who</em> can approve, but each level may fall back to certain
/// lower levels when the primary approver is unavailable.
/// </summary>
public enum PermissionClearance
{
    /// <summary>
    /// No clearance has been configured — soft deny.  The system cascades
    /// to the next permission layer (channel → context → role).  If every
    /// layer is <c>Unset</c>, the action is denied.
    /// </summary>
    Unset = 0,

    /// <summary>
    /// Requires approval from a user who holds the same permission.
    /// This is the only user-level check — no agent can satisfy it.
    /// </summary>
    ApprovedBySameLevelUser = 1,

    /// <summary>
    /// Requires approval from a user on this permission group's user
    /// whitelist (the user does not need to hold the permission
    /// themselves).
    /// <para>
    /// <b>Fallback:</b> if no whitelisted user approves, a same-level
    /// user (<see cref="ApprovedBySameLevelUser"/>) can also approve.
    /// </para>
    /// <para>
    /// Channel/context pre-authorisation counts at this level: the user
    /// who can access the channel or context permission is treated
    /// as a whitelisted user.
    /// </para>
    /// </summary>
    ApprovedByWhitelistedUser = 2,

    /// <summary>
    /// Requires approval from another agent that holds the same
    /// permission.  <b>Agent-only</b> — no user (same-level or
    /// whitelisted) can satisfy this level.
    /// </summary>
    ApprovedByPermittedAgent = 3,

    /// <summary>
    /// Requires approval from an agent on this permission group's agent
    /// whitelist.
    /// <para>
    /// <b>Fallback chain:</b> if the whitelisted-agent check fails, the
    /// system tries (in order) permitted agent
    /// (<see cref="ApprovedByPermittedAgent"/>), whitelisted user
    /// (<see cref="ApprovedByWhitelistedUser"/>), then same-level user
    /// (<see cref="ApprovedBySameLevelUser"/>).
    /// </para>
    /// </summary>
    ApprovedByWhitelistedAgent = 4,

    /// <summary>
    /// The agent can act independently without any external approval.
    /// </summary>
    Independent = 5,

    /// <summary>
    /// Hard deny.  The action is denied outright and the system does
    /// <b>not</b> cascade to the next permission layer.  Use this when
    /// a specific layer must block the action regardless of what parent
    /// layers allow.
    /// </summary>
    Restricted = 6
}
