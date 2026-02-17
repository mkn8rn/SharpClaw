using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// Grants a role a specific access level to an <see cref="ExternalInformationStoreDB"/>.
/// </summary>
public class ExternalInfoStoreAccessDB : BaseEntity
{
    public InfoStoreAccessLevel AccessLevel { get; set; }

    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid RolePermissionsId { get; set; }
    public RolePermissionsDB RolePermissions { get; set; } = null!;

    public Guid ExternalInformationStoreId { get; set; }
    public ExternalInformationStoreDB ExternalInformationStore { get; set; } = null!;
}
