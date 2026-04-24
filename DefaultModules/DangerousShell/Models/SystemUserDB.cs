using SharpClaw.Contracts.Entities;

namespace SharpClaw.Modules.DangerousShell.Models;

/// <summary>
/// A registered operating-system user account that agents can execute
/// terminal commands as via the dangerous (unsandboxed) shell.
/// </summary>
public class SystemUserDB : BaseEntity
{
    public required string Username { get; set; }
    public string? Description { get; set; }

    public string? SandboxRoot { get; set; }
    public string? WorkingDirectory { get; set; }
    public int DefaultStepTimeoutSeconds { get; set; } = 30;
    public int DefaultMaxRetries { get; set; }

    // FK to host SkillDB — bare Guid, no nav property
    public Guid? SkillId { get; set; }
}
