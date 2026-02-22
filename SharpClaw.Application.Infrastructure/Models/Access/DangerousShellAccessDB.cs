using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Infrastructure.Models.Access;

/// <summary>
/// Grants a role the ability to execute commands through a dangerous
/// (unrestricted) shell as a specific operating-system user.
/// <para>
/// Dangerous shells (Bash, PowerShell, CommandPrompt, Git) spawn real
/// interpreter processes with arbitrary command text.  They are never
/// routed through mk8.shell.  For sandboxed execution see
/// <see cref="SafeShellAccessDB"/>.
/// </para>
/// </summary>
public class DangerousShellAccessDB : BaseEntity
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
    /// The dangerous shell type this grant authorises (Bash, PowerShell,
    /// CommandPrompt, or Git).
    /// </summary>
    public DangerousShellType ShellType { get; set; }
}
