using Microsoft.EntityFrameworkCore;
using SharpClaw.Modules.Mk8Shell.Contracts;

namespace SharpClaw.Modules.Mk8Shell.Services;

/// <summary>
/// Resolves container IDs from <see cref="Mk8ShellDbContext"/> by sandbox name.
/// </summary>
internal sealed class ContainerSandboxResolver(Mk8ShellDbContext db) : IContainerSandboxResolver
{
    public Task<Guid?> GetContainerIdBySandboxNameAsync(
        ContainerType type, string sandboxName, CancellationToken ct = default) =>
        db.Containers
            .Where(c => c.Type == type && c.SandboxName == sandboxName)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
}
