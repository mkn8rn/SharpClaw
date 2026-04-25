namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side service for container ownership provisioning.
/// Implemented by <c>ContainerOwnershipService</c> in Core/Infrastructure;
/// injected into modules via DI so they never reference Core entities.
/// </summary>
public interface IContainerProvisioner
{
    /// <summary>
    /// Creates an owner role and permission set for <paramref name="containerId"/>,
    /// then optionally assigns the role to the current session user if they
    /// have no existing role.
    /// </summary>
    Task CreateOwnerRoleAsync(
        Guid containerId,
        string containerName,
        string accessContainerActionName,
        string executeSafeShellActionName,
        string containerTypeKey,
        Guid? userId,
        CancellationToken ct = default);
}
