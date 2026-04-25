namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Describes a resource type owned by a module. Used to build grant labels
/// for chat headers, to resolve wildcard grants (AllResources) into
/// concrete resource IDs at runtime, and to map delegate method names
/// to resource type strings for permission evaluation.
/// <para>
/// Modules return these from <see cref="ISharpClawModule.GetResourceTypeDescriptors"/>
/// during registration. The host stores them in <c>ModuleRegistry</c>
/// and consumers (<c>HeaderTagProcessor</c>, <c>ChatService</c>,
/// <c>AgentActionService</c>, <c>SeedingService</c>) query the registry
/// instead of maintaining hardcoded magic-string arrays.
/// </para>
/// </summary>
/// <param name="ResourceType">
/// String discriminator stored in <c>ResourceAccessDB.ResourceType</c>.
/// Must be unique across all modules.
/// </param>
/// <param name="GrantLabel">
/// Human-readable label used in the chat header grant list.
/// </param>
/// <param name="DelegateMethodName">
/// The <c>DelegateTo</c> method name that maps to this resource type
/// in the permission evaluation pipeline.
/// <c>AgentActionService</c> uses this to resolve per-resource
/// permission checks dynamically at runtime.
/// </param>
/// <param name="LoadAllIds">
/// Async callback that loads all resource IDs of this type from the
/// database. Receives the scoped <see cref="IServiceProvider"/> so
/// the module can resolve its own <c>DbContext</c> or services.
/// Called when a wildcard grant (AllResources) needs to be expanded.
/// </param>
/// <param name="LoadLookupItems">
/// Optional async callback that loads <c>(Id, Name)</c> pairs for the
/// generic <c>GET /resources/lookup/{type}</c> endpoint.  When <c>null</c>
/// the lookup endpoint omits this resource type.
/// </param>
/// <param name="DefaultResourceKey">
/// Optional case-insensitive key used in the generic default-resource
/// registry (e.g. <c>"container"</c>, <c>"website"</c>).  When set,
/// callers may reference this resource type by this short key when
/// configuring per-channel or per-context defaults.  Must be unique
/// across all registered modules.  When <c>null</c> the resource type
/// is not addressable as a default resource entry.
/// </param>
public sealed record ModuleResourceTypeDescriptor(
    string ResourceType,
    string GrantLabel,
    string DelegateMethodName,
    Func<IServiceProvider, CancellationToken, Task<List<Guid>>> LoadAllIds,
    Func<IServiceProvider, CancellationToken, Task<List<(Guid Id, string Name)>>>? LoadLookupItems = null,
    string? DefaultResourceKey = null);
