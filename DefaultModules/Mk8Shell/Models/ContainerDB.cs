using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.Mk8Shell.Contracts;

namespace SharpClaw.Modules.Mk8Shell.Models;

/// <summary>
/// A registered sandbox, VM, or container environment for isolated
/// code execution.
/// </summary>
public class ContainerDB : BaseEntity
{
    public required string Name { get; set; }
    public ContainerType Type { get; set; } = ContainerType.Mk8Shell;

    /// <summary>
    /// For <see cref="ContainerType.Mk8Shell"/>: the sandbox name
    /// registered via mk8.shell.startup.
    /// </summary>
    public string? SandboxName { get; set; }

    public string? Image { get; set; }
    public string? Endpoint { get; set; }
    public string? Description { get; set; }

    // FK to host SkillDB — bare Guid, no nav property
    public Guid? SkillId { get; set; }
}
