using SharpClaw.Contracts.Persistence;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Merges the host's static <see cref="ColdEntityIndex.IndexedProperties"/> with
/// zero or more <see cref="IModuleColdIndexContributor"/> registrations contributed
/// by loaded modules.
/// <para>
/// The combined dictionary is built once on first access and cached. Modules
/// register contributors during DI setup; Infrastructure never needs to know
/// which specific module entities exist.
/// </para>
/// </summary>
internal sealed class ModuleColdIndexRegistry
{
    private readonly IReadOnlyList<IModuleColdIndexContributor> _contributors;
    private IReadOnlyDictionary<string, string[]>? _merged;

    public ModuleColdIndexRegistry(IEnumerable<IModuleColdIndexContributor> contributors)
    {
        _contributors = contributors.ToList();
    }

    /// <summary>
    /// Returns the merged index map: host static entries plus all module contributions.
    /// Duplicate entity-type keys are merged (property arrays are unioned, de-duplicated).
    /// </summary>
    public IReadOnlyDictionary<string, string[]> GetIndexedProperties()
    {
        if (_merged is not null)
            return _merged;

        var combined = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Seed from host static definitions.
        foreach (var (typeName, props) in ColdEntityIndex.IndexedProperties)
        {
            if (!combined.TryGetValue(typeName, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                combined[typeName] = set;
            }
            foreach (var p in props)
                set.Add(p);
        }

        // Merge module contributions.
        foreach (var contributor in _contributors)
        {
            foreach (var (typeName, props) in contributor.GetIndexedProperties())
            {
                if (!combined.TryGetValue(typeName, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    combined[typeName] = set;
                }
                foreach (var p in props)
                    set.Add(p);
            }
        }

        _merged = combined.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToArray(),
            StringComparer.Ordinal);

        return _merged;
    }
}
