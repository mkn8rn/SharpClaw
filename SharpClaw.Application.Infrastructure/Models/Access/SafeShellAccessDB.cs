using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Infrastructure.Models.Access;

/// <summary>
/// Grants a role the ability to execute commands through a safe
/// (sandboxed, verb-restricted) shell as a specific operating-system user.
/// </summary>
public class SafeShellAccessDB : BaseEntity
{
    /// <summary>
    /// Per-permission clearance override.
    /// <see cref="PermissionClearance.Unset"/> falls back to the group default.
    /// </summary>
    public PermissionClearance Clearance { get; set; } = PermissionClearance.Unset;

    public Guid PermissionSetId { get; set; }
    public PermissionSetDB PermissionSet { get; set; } = null!;

    public Guid SystemUserId { get; set; }
    public SystemUserDB SystemUser { get; set; } = null!;

    /// <summary>
    /// The safe shell type this grant authorises. Currently only
    /// <see cref="SafeShellType.Mk8Shell"/> but extensible for future
    /// safe shell languages.
    /// </summary>
    public SafeShellType ShellType { get; set; }
}
