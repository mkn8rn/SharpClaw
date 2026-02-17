namespace SharpClaw.Contracts.DTOs.Models;

public sealed record CreateModelRequest(string Name, Guid ProviderId);
public sealed record UpdateModelRequest(string? Name = null);
public sealed record ModelResponse(Guid Id, string Name, Guid ProviderId, string ProviderName);
