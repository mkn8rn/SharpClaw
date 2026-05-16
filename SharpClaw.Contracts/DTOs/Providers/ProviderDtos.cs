using SharpClaw.Contracts.Providers;

namespace SharpClaw.Contracts.DTOs.Providers;

/// <param name="ApiEndpoint">Required when the selected provider key requires an endpoint.</param>
public sealed record CreateProviderRequest(string Name, string ProviderKey, string? ApiEndpoint = null, string? ApiKey = null);
public sealed record UpdateProviderRequest(string? Name = null, string? ApiEndpoint = null);
public sealed record SetApiKeyRequest(string ApiKey);
public sealed record ProviderResponse(Guid Id, string Name, string ProviderKey, string? ApiEndpoint, bool HasApiKey);
public sealed record ProviderTypeResponse(
    string ProviderKey,
    string DisplayName,
    bool RequiresEndpoint,
    bool SupportsAutomaticEndpointDiscovery,
    bool RequiresApiKey,
    bool SupportsDeviceCodeAuth);
