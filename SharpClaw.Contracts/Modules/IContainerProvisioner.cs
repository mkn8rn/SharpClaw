using SharpClaw.Contracts.DTOs.Containers;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side service for container ownership provisioning.
/// Implemented by <c>ContainerOwnershipService</c> in Core/Infrastructure;
/// injected into the Mk8Shell module so it never references Core entities.
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
        ContainerType containerType,
        Guid? userId,
        CancellationToken ct = default);
}
