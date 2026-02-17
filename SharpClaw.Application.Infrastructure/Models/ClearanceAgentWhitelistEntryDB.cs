using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// A whitelisted agent on a <see cref="RolePermissionsDB"/> group.
/// Agents on this list can approve other agents' actions at
/// <see cref="Contracts.Enums.PermissionClearance.ApprovedByWhitelistedAgent"/>.
/// </summary>
public class ClearanceAgentWhitelistEntryDB : BaseEntity
{
    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid AgentId { get; set; }
    public AgentDB Agent { get; set; } = null!;
}
