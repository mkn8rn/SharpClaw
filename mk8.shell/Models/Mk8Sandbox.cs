namespace Mk8.Shell.Models;

/// <summary>
/// Represents a registered mk8.shell sandbox — a rooted, isolated
/// workspace directory that has been provisioned by mk8.shell.startup.
/// <para>
/// A sandbox is identified by a unique <see cref="Id"/> (e.g. "Banana")
/// and bound to a specific local machine via a cryptographically signed
/// environment file. Every mk8.shell command begins with a sandbox ID
/// so the runtime knows which root path, env vars, and signing context
/// to load.
/// </para>
/// </summary>
public sealed class Mk8Sandbox
{
    /// <summary>
    /// Human-readable sandbox identifier. Must be unique within the
    /// local registry. Used as the first argument to every mk8.shell
    /// command (e.g. <c>Banana FileRead …</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Absolute path to the sandbox root directory on the local machine.
    /// All file/dir operations are jailed inside this path.
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// UTC timestamp of when this sandbox was registered via
    /// mk8.shell.startup.
    /// </summary>
    public required DateTimeOffset RegisteredAtUtc { get; init; }
}
