namespace SharpClaw.Contracts.DTOs.DefaultResources;

/// <summary>
/// Sets default resources for a channel or context.  Keys are the
/// module-contributed default-resource keys (e.g. registered via
/// <c>ModuleResourceTypeDescriptor.DefaultResourceKey</c>).  A
/// <c>null</c> value clears the existing default for that key.
/// </summary>
public sealed record SetDefaultResourcesRequest(
    IReadOnlyDictionary<string, Guid?> Entries);

/// <summary>
/// The resolved default resources for a channel or context, keyed by
/// module-contributed default-resource key.
/// </summary>
public sealed record DefaultResourcesResponse(
    Guid Id,
    IReadOnlyDictionary<string, Guid> Entries);

/// <summary>
/// Sets a single default resource by key.
/// </summary>
public sealed record SetDefaultResourceByKeyRequest(Guid ResourceId);

