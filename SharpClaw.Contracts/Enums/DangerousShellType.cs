namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Shell types that are considered "dangerous" — they allow arbitrary
/// command execution and require system-user credentials plus elevated
/// clearance.
/// </summary>
public enum DangerousShellType
{
    Bash,
    PowerShell,
    CommandPrompt,
    Git
}
