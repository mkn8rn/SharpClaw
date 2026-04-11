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
/// String discriminator stored in <c>ResourceAccessDB.ResourceType</c>
/// (e.g. <c>"DsShell"</c>, <c>"WaWebsite"</c>). Must be unique across
/// all modules.
/// </param>
/// <param name="GrantLabel">
/// Human-readable label used in the chat header grant list
/// (e.g. <c>"DangerousShell"</c>, <c>"WebsiteAccess"</c>).
/// </param>
/// <param name="DelegateMethodName">
/// The <c>DelegateTo</c> method name that maps to this resource type
/// in the permission evaluation pipeline (e.g.
/// <c>"UnsafeExecuteAsDangerousShellAsync"</c> → <c>"DsShell"</c>).
/// <c>AgentActionService</c> uses this to resolve per-resource
/// permission checks dynamically at runtime.
/// </param>
/// <param name="LoadAllIds">
/// Async callback that loads all resource IDs of this type from the
/// database. Receives the scoped <see cref="IServiceProvider"/> so
/// the module can resolve its own <c>DbContext</c> or services.
/// Called when a wildcard grant (AllResources) needs to be expanded.
/// </param>
public sealed record ModuleResourceTypeDescriptor(
    string ResourceType,
    string GrantLabel,
    string DelegateMethodName,
    Func<IServiceProvider, CancellationToken, Task<List<Guid>>> LoadAllIds);
