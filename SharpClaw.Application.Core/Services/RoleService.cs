using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharpClaw.Core.Permissions;
using SharpClaw.Contracts.Entities.Core.Clearance;
using SharpClaw.Contracts.DTOs.Roles;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

/// <summary>
/// Manages roles and their permission sets. Enforces the rule that a
/// user can only grant permissions they already hold themselves.
/// </summary>
public sealed class RoleService(
    SharpClawDbContext db,
    IConfiguration configuration,
    RolePermissionAdministrationEngine rolePermissions,
    ChatRuntimeInvalidationPlanner invalidations,
    ChatCache chatCache)
{
    // ═══════════════════════════════════════════════════════════════
    // Read
    // ═══════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct = default)
    {
        return await db.Roles
            .OrderBy(r => r.Name)
            .Select(rolePermissions.ToResponseProjection())
            .ToListAsync(ct);
    }

    /// <summary>
    /// Creates a new role with an empty permission set.
    /// </summary>
    public async Task<RoleResponse> CreateAsync(
        string name, CancellationToken ct = default)
    {
        if (IsUniqueRoleNamesEnforced())
            await EnsureRoleNameUniqueAsync(name, excludeId: null, ct);

        var role = rolePermissions.CreateRole(name);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);

        return rolePermissions.ToResponse(role);
    }

    public async Task<RoleResponse?> GetByIdAsync(
        Guid roleId, CancellationToken ct = default)
    {
        return await db.Roles
            .Where(r => r.Id == roleId)
            .Select(rolePermissions.ToResponseProjection())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RolePermissionsResponse?> GetPermissionsAsync(
        Guid roleId, CancellationToken ct = default)
    {
        var role = await db.Roles
            .Include(r => r.PermissionSet)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

        if (role is null)
            return null;

        var ps = role.PermissionSet is not null
            ? await LoadFullPermissionSetAsync(role.PermissionSet.Id, ct)
            : null;

        return rolePermissions.ToPermissionsResponse(role, ps);
    }

    // ═══════════════════════════════════════════════════════════════
    // Set permissions
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Replaces the entire permission set of <paramref name="roleId"/>
    /// with the values in <paramref name="request"/>. The calling user
    /// (<paramref name="callerUserId"/>) must hold every permission
    /// they are granting — you cannot give what you don't have.
    /// </summary>
    public async Task<RolePermissionsResponse?> SetPermissionsAsync(
        Guid roleId, SetRolePermissionsRequest request,
        Guid callerUserId, CancellationToken ct = default)
    {
        var role = await db.Roles
            .Include(r => r.PermissionSet)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

        if (role is null)
            return null;

        // Admin users bypass permission validation — they can grant anything.
        var isAdmin = await db.Users.AnyAsync(u => u.Id == callerUserId && u.IsUserAdmin, ct);
        if (!isAdmin)
        {
            // Load caller's own permission set for validation.
            var callerPs = await LoadCallerPermissionSetAsync(callerUserId, ct);

            rolePermissions.ValidateRequestedGrants(request, callerPs);
        }

        // Reconcile the permission set via incremental diff — never deletes wildcard grants.
        PermissionSetDB ps;
        if (role.PermissionSetId is { } existingPsId)
        {
            ps = await LoadFullPermissionSetAsync(existingPsId, ct)
                ?? throw new InvalidOperationException(
                    $"Permission set {existingPsId} not found.");
        }
        else
        {
            ps = rolePermissions.CreatePermissionSetForRole(role);
            db.PermissionSets.Add(ps);
        }

        rolePermissions.ReconcilePermissionSet(ps, request);

        await db.SaveChangesAsync(ct);
        InvalidatePermissionRuntimeState();

        return rolePermissions.ToPermissionsResponse(role, ps);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<PermissionSetDB?> LoadCallerPermissionSetAsync(
        Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user?.Role?.PermissionSetId is not { } psId)
            return null;

        return await LoadFullPermissionSetAsync(psId, ct);
    }

    private async Task<PermissionSetDB?> LoadFullPermissionSetAsync(
        Guid psId, CancellationToken ct)
    {
        return await db.PermissionSets
            .Include(p => p.GlobalFlags)
            .Include(p => p.ResourceAccesses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == psId, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Mutate
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Renames a role. Uniqueness is enforced when configured.
    /// </summary>
    public async Task<RoleResponse?> RenameAsync(
        Guid roleId, string newName, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null)
            return null;

        if (IsUniqueRoleNamesEnforced())
            await EnsureRoleNameUniqueAsync(newName, excludeId: roleId, ct);

        rolePermissions.RenameRole(role, newName);
        await db.SaveChangesAsync(ct);
        InvalidatePermissionRuntimeState();
        return rolePermissions.ToResponse(role);
    }

    /// <summary>
    /// Deletes a role. The permission set owned by the role is also removed.
    /// Users assigned to this role will have their RoleId cleared.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await db.Roles
            .Include(r => r.PermissionSet)
                .ThenInclude(ps => ps!.GlobalFlags)
            .Include(r => r.PermissionSet)
                .ThenInclude(ps => ps!.ResourceAccesses)
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

        if (role is null)
            return false;

        var deletion = rolePermissions.PlanDeleteRole(role);
        if (deletion.PermissionSet is not null)
        {
            db.RemoveRange(deletion.GlobalFlags);
            db.RemoveRange(deletion.ResourceAccesses);
            db.PermissionSets.Remove(deletion.PermissionSet);
        }

        db.Roles.Remove(deletion.Role);
        await db.SaveChangesAsync(ct);
        InvalidatePermissionRuntimeState();
        return true;
    }

    private bool IsUniqueRoleNamesEnforced()
    {
        return RolePermissionAdministrationEngine.IsUniqueRoleNameEnforced(
            configuration["UniqueNames:Roles"]);
    }

    private void InvalidatePermissionRuntimeState()
    {
        invalidations.PermissionSetsChanged().ApplyTo(chatCache);
    }

    private async Task EnsureRoleNameUniqueAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var names = await db.Roles
            .Where(r => excludeId == null || r.Id != excludeId)
            .Select(r => r.Name)
            .ToListAsync(ct);
        rolePermissions.EnsureRoleNameAvailable(name, names);
    }
}
