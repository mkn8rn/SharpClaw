using SharpClaw.Modules.WebAccess.Enums;

namespace SharpClaw.Modules.WebAccess.Dtos;

// ── Requests ──────────────────────────────────────────────────────

public sealed record CreateSearchEngineRequest(
    string Name,
    SearchEngineType Type,
    string Endpoint,
    string? Description = null,
    string? ApiKey = null,
    string? SecondaryKey = null);

public sealed record UpdateSearchEngineRequest(
    string? Name = null,
    SearchEngineType? Type = null,
    string? Endpoint = null,
    string? Description = null,
    string? ApiKey = null,
    string? SecondaryKey = null);

// ── Responses ─────────────────────────────────────────────────────

public sealed record SearchEngineResponse(
    Guid Id,
    string Name,
    SearchEngineType Type,
    string Endpoint,
    string? Description,
    bool HasApiKey,
    bool HasSecondaryKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
