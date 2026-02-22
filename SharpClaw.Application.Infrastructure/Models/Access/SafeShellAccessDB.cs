using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Infrastructure.Models.Access;

/// <summary>
/// Grants a role the ability to execute commands through a safe
/// (sandboxed, verb-restricted) shell targeting a specific container.
/// <para>
/// Safe shells are exclusively mk8.shell — a closed-verb DSL that
/// never invokes a real shell interpreter.  The <see cref="ContainerId"/>
/// identifies which sandbox the execution is permitted in.  For
/// unrestricted shell access see <see cref="DangerousShellAccessDB"/>.
/// </para>
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

    /// <summary>
    /// The container (sandbox) this grant authorises execution in.
    /// </summary>
    public Guid ContainerId { get; set; }
    public ContainerDB Container { get; set; } = null!;

    /// <summary>
    /// The safe shell type this grant authorises. Currently only
    /// <see cref="SafeShellType.Mk8Shell"/> but extensible for future
    /// safe shell languages.
    /// </summary>
    public SafeShellType ShellType { get; set; }
}
