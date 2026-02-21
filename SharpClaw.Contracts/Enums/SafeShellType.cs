namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Shell types that are considered "safe" — they execute within a
/// sandboxed, verb-restricted environment.  New safe languages can be
/// added here without changing the permission model.
/// </summary>
public enum SafeShellType
{
    Mk8Shell
}
