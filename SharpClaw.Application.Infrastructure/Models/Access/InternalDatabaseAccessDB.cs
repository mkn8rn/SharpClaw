using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Infrastructure.Models.Access;

/// <summary>
/// Grants a role a specific access level to an <see cref="InternalDatabaseDB"/>.
/// </summary>
public class InternalDatabaseAccessDB : BaseEntity
{
    public DatabaseAccessLevel AccessLevel { get; set; }

    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid PermissionSetId { get; set; }
    public PermissionSetDB PermissionSet { get; set; } = null!;

    public Guid InternalDatabaseId { get; set; }
    public InternalDatabaseDB InternalDatabase { get; set; } = null!;
}
