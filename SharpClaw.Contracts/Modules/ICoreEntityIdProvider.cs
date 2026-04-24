namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side provider for enumerating core pipeline entity IDs and names.
/// Used by module <see cref="ModuleResourceTypeDescriptor.LoadAllIds"/> and
/// <see cref="ModuleResourceTypeDescriptor.LoadLookupItems"/> lambdas to
/// expand wildcard grants without taking an Infrastructure reference.
/// </summary>
public interface ICoreEntityIdProvider
{
    Task<List<Guid>> GetAgentIdsAsync(CancellationToken ct = default);
    Task<List<Guid>> GetChannelIdsAsync(CancellationToken ct = default);
    Task<List<(Guid Id, string Name)>> GetAgentLookupItemsAsync(CancellationToken ct = default);
    Task<List<(Guid Id, string Name)>> GetChannelLookupItemsAsync(CancellationToken ct = default);
}
