using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.Providers;

/// <param name="ApiEndpoint">Required for <see cref="ProviderType.Custom"/> providers. Ignored for built-in types.</param>
public sealed record CreateProviderRequest(string Name, ProviderType ProviderType, string? ApiEndpoint = null, string? ApiKey = null);
public sealed record UpdateProviderRequest(string? Name = null, string? ApiEndpoint = null);
public sealed record SetApiKeyRequest(string ApiKey);
public sealed record ProviderResponse(Guid Id, string Name, ProviderType ProviderType, string? ApiEndpoint, bool HasApiKey);
