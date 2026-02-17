using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// Grants a role the ability to execute terminal commands as a specific
/// operating-system user.
/// </summary>
public class SystemUserAccessDB : BaseEntity
{
    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid SystemUserId { get; set; }
    public SystemUserDB SystemUser { get; set; } = null!;
}
