using SharpClaw.Modules.Mk8Shell.Contracts;

namespace SharpClaw.Modules.Mk8Shell.Dtos;

public sealed record CreateContainerRequest(
    ContainerType Type,
    string Name,
    string? Path = null,
    string? Description = null);

public sealed record UpdateContainerRequest(
    string? Name = null,
    string? Description = null);

public sealed record ContainerResponse(
    Guid Id,
    ContainerType Type,
    string Name,
    string? SandboxName,
    string? LocalPath,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record ContainerSyncResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> ImportedNames,
    IReadOnlyList<string> SkippedNames);
