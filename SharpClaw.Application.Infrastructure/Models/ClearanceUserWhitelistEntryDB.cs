using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// A whitelisted user on a <see cref="RolePermissionsDB"/> group.
/// Users on this list can approve agent actions at
/// <see cref="Contracts.Enums.PermissionClearance.ApprovedByWhitelistedUser"/>
/// even if they do not hold the permission themselves.
/// </summary>
public class ClearanceUserWhitelistEntryDB : BaseEntity
{
    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid UserId { get; set; }
    public UserDB User { get; set; } = null!;
}
