namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Lightweight module state descriptor returned by lifecycle operations.
/// </summary>
public sealed record ModuleStateResponse(
    string ModuleId,
    string DisplayName,
    string ToolPrefix,
    bool Enabled,
    string? Version,
    bool Registered,
    bool IsExternal,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);
