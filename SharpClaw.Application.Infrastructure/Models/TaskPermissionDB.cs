using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// Grants a role edit access to a specific scheduled task.
/// </summary>
public class TaskPermissionDB : BaseEntity
{
    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid ScheduledTaskId { get; set; }
    public ScheduledTaskDB ScheduledTask { get; set; } = null!;
}
