using SharpClaw.Modules.Mk8Shell.Contracts;

namespace SharpClaw.Modules.Mk8Shell.Services;

/// <summary>
/// Resolves container IDs from the Mk8Shell module's data store by sandbox name.
/// </summary>
internal interface IContainerSandboxResolver
{
    /// <summary>
    /// Returns the container ID whose <c>SandboxName</c> matches <paramref name="sandboxName"/>
    /// and whose <c>Type</c> matches <paramref name="type"/>, or <see langword="null"/> if not found.
    /// </summary>
    Task<Guid?> GetContainerIdBySandboxNameAsync(
        ContainerType type, string sandboxName, CancellationToken ct = default);
}
