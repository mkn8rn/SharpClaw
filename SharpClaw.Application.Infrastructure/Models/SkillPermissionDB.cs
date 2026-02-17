using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// Grants a role access to a specific <see cref="SkillDB"/>.
/// </summary>
public class SkillPermissionDB : BaseEntity
{
    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid SkillId { get; set; }
    public SkillDB Skill { get; set; } = null!;
}
