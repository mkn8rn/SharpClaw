namespace SharpClaw.Modules.Mk8Shell.Contracts;

/// <summary>
/// Types of containers supported by the Mk8Shell module. Each type has its
/// own registration and lifecycle semantics.
/// </summary>
public enum ContainerType
{
    /// <summary>
    /// An mk8.shell sandbox — a restricted pseudocode shell environment
    /// registered via mk8.shell.startup. Only the sandbox name is stored
    /// in the database; the local path is resolved at runtime from the
    /// machine's <c>%APPDATA%/mk8.shell/sandboxes.json</c>.
    /// </summary>
    Mk8Shell = 0,
}
