using Mk8.Shell.Engine;

namespace Mk8.Shell.Safety;

/// <summary>
/// Server-side registry of admin-approved script fragments for the
/// <see cref="Mk8ShellVerb.Include"/> verb.
/// <para>
/// Agents cannot define fragments — they can only reference them by ID.
/// Fragments go through the same compilation pipeline as all other
/// operations. This is compile-time inlining, not runtime function calls.
/// </para>
/// </summary>
public interface IMk8FragmentRegistry
{
    /// <summary>
    /// Attempts to retrieve an approved fragment by its ID.
    /// </summary>
    /// <param name="fragmentId">
    /// The admin-assigned identifier, e.g. <c>"setup-workspace"</c>,
    /// <c>"deploy-to-staging"</c>.
    /// </param>
    /// <param name="operations">
    /// The fragment's operations if found; <c>null</c> otherwise.
    /// </param>
    /// <returns><c>true</c> if the fragment exists.</returns>
    bool TryGetFragment(
        string fragmentId,
        out IReadOnlyList<Mk8ShellOperation>? operations);
}

/// <summary>
/// In-memory fragment registry for testing and simple deployments.
/// Production deployments should back this with a persistent store.
/// </summary>
public sealed class Mk8InMemoryFragmentRegistry : IMk8FragmentRegistry
{
    private readonly Dictionary<string, IReadOnlyList<Mk8ShellOperation>> _fragments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an admin-approved fragment. This is a server-side
    /// operation — agents never call this.
    /// </summary>
    public void Register(string fragmentId, IReadOnlyList<Mk8ShellOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentId);
        ArgumentNullException.ThrowIfNull(operations);

        ValidateFragmentId(fragmentId);

        if (operations.Count == 0)
            throw new ArgumentException(
                "Fragment must contain at least one operation.", nameof(operations));

        // Fragments cannot contain Include (no recursive composition).
        foreach (var op in operations)
        {
            if (op.Verb == Mk8ShellVerb.Include)
                throw new ArgumentException(
                    $"Fragment '{fragmentId}' contains an Include verb. " +
                    "Nested includes are not allowed.", nameof(operations));
        }

        _fragments[fragmentId] = operations;
    }

    /// <summary>
    /// Removes a fragment. Server-side only.
    /// </summary>
    public bool Unregister(string fragmentId) =>
        _fragments.Remove(fragmentId);

    public bool TryGetFragment(
        string fragmentId,
        out IReadOnlyList<Mk8ShellOperation>? operations) =>
        _fragments.TryGetValue(fragmentId, out operations);

    /// <summary>
    /// Returns all registered fragment IDs.
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredIds() =>
        _fragments.Keys;

    private static void ValidateFragmentId(string id)
    {
        if (id.Length > 128)
            throw new ArgumentException(
                $"Fragment ID '{id}' exceeds maximum length of 128 characters.",
                nameof(id));

        foreach (var c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                throw new ArgumentException(
                    $"Fragment ID '{id}' contains invalid character '{c}'. " +
                    "IDs may only contain letters, digits, hyphens, underscores, and periods.",
                    nameof(id));
        }
    }
}
