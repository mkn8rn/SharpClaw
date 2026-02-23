using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.Channels;

public sealed record CreateChannelRequest(
    Guid AgentId,
    string? Title = null,
    Guid? ModelId = null,
    Guid? ContextId = null,
    Guid? PermissionSetId = null);

public sealed record UpdateChannelRequest(
    string? Title = null,
    Guid? ModelId = null,
    Guid? ContextId = null,
    Guid? PermissionSetId = null);

public sealed record ChannelResponse(
    Guid Id,
    string Title,
    Guid AgentId,
    string AgentName,
    Guid ModelId,
    string ModelName,
    Guid? ContextId,
    string? ContextName,
    Guid? PermissionSetId,
    Guid? EffectivePermissionSetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
