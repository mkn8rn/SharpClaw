using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// Grants a role CRUD access to a specific agent (sub-agent management).
/// Sub-agents can only be created with permissions ≤ the creator's own.
/// </summary>
public class AgentPermissionDB : BaseEntity
{
    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid AgentId { get; set; }
    public AgentDB Agent { get; set; } = null!;
}
