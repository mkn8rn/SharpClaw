using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.Containers;

// ── Requests ──────────────────────────────────────────────────────

public sealed record CreateContainerRequest(
    ContainerType Type,
    /// <summary>
    /// For <see cref="ContainerType.Mk8Shell"/>: the sandbox name
    /// (e.g. "Banana"). Must be English letters and numbers only.
    /// </summary>
    string Name,
    /// <summary>
    /// For <see cref="ContainerType.Mk8Shell"/>: the parent directory
    /// where the sandbox folder will be created (e.g. "D:/"). The
    /// sandbox is created at <c>{Path}/{Name}/</c>.
    /// </summary>
    string? Path = null,
    string? Description = null);

public sealed record UpdateContainerRequest(
    string? Name = null,
    string? Description = null);

// ── Responses ─────────────────────────────────────────────────────

public sealed record ContainerResponse(
    Guid Id,
    ContainerType Type,
    string Name,
    /// <summary>
    /// For <see cref="ContainerType.Mk8Shell"/>: the sandbox name
    /// stored in the database. Same as <see cref="Name"/> for mk8shell
    /// containers.
    /// </summary>
    string? SandboxName,
    /// <summary>
    /// Local sandbox path resolved from <c>%APPDATA%/mk8.shell</c>.
    /// Only populated when the sandbox is registered on this machine.
    /// <c>null</c> when the sandbox is not locally available.
    /// </summary>
    string? LocalPath,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record ContainerSyncResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> ImportedNames,
    IReadOnlyList<string> SkippedNames);
