namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Defines the level of external approval an agent requires before it can
/// act on a granted permission.
/// </summary>
public enum PermissionClearance
{
    /// <summary>Not set / unknown. Falls back to the group-level default.</summary>
    Unset = 0,

    /// <summary>
    /// Requires approval from a user who holds the same permission level.
    /// </summary>
    ApprovedBySameLevelUser = 1,

    /// <summary>
    /// Requires approval from a user on this permission group's user whitelist
    /// (the user does not need to hold the permission themselves).
    /// Includes <see cref="ApprovedBySameLevelUser"/>.
    /// </summary>
    ApprovedByWhitelistedUser = 2,

    /// <summary>
    /// Requires approval from another agent that holds the same permission.
    /// Includes <see cref="ApprovedBySameLevelUser"/>.
    /// </summary>
    ApprovedByPermittedAgent = 3,

    /// <summary>
    /// Requires approval from an agent on this permission group's agent whitelist.
    /// Includes <see cref="ApprovedBySameLevelUser"/>,
    /// <see cref="ApprovedByWhitelistedUser"/>, and
    /// <see cref="ApprovedByPermittedAgent"/>.
    /// </summary>
    ApprovedByWhitelistedAgent = 4,

    /// <summary>
    /// The agent can act independently without any external approval.
    /// </summary>
    Independent = 5
}
