namespace SharpClaw.Contracts.DTOs.Models;

public sealed record CreateModelRequest(
    string Name,
    Guid ProviderId,
    string? CustomId = null,
    IReadOnlySet<string>? CapabilityTags = null);

public sealed record UpdateModelRequest(
    string? Name = null,
    string? CustomId = null,
    IReadOnlySet<string>? CapabilityTags = null);

public sealed record ModelResponse(
    Guid Id,
    string Name,
    Guid ProviderId,
    string ProviderName,
    string? CustomId = null,
    IReadOnlySet<string>? CapabilityTags = null);
