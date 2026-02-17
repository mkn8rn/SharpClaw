using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

/// <summary>
/// One-to-one with <see cref="RoleDB"/>. Defines what actions holders of
/// this role (users or agents) are permitted to perform.
/// </summary>
public class RolePermissionsDB : BaseEntity
{
    // ── Back-reference ────────────────────────────────────────────
    public Guid RoleId { get; set; }
    public RoleDB Role { get; set; } = null!;

    // ── Default clearance ─────────────────────────────────────────

    /// <summary>
    /// Fallback clearance level used when an individual permission's
    /// <see cref="PermissionClearance"/> is <see cref="PermissionClearance.Unset"/>.
    /// </summary>
    public PermissionClearance DefaultClearance { get; set; } = PermissionClearance.Unset;

    // ── Global flags ──────────────────────────────────────────────

    /// <summary>Execute shell commands as administrator / sudo.</summary>
    public bool CanExecuteAsAdmin { get; set; }

    /// <summary>Create sub-agents (must have ≤ the creator's permissions).</summary>
    public bool CanCreateSubAgents { get; set; }

    /// <summary>Create new sandbox / VM / container environments.</summary>
    public bool CanCreateContainers { get; set; }

    /// <summary>Register new local or external information stores.</summary>
    public bool CanRegisterInfoStores { get; set; }

    /// <summary>Edit any scheduled task (when false, per-task grants apply).</summary>
    public bool CanEditAllTasks { get; set; }

    // ── Per-resource grant collections ────────────────────────────

    public ICollection<SystemUserAccessDB> SystemUserAccesses { get; set; } = [];
    public ICollection<LocalInfoStoreAccessDB> LocalInfoStorePermissions { get; set; } = [];
    public ICollection<ExternalInfoStoreAccessDB> ExternalInfoStorePermissions { get; set; } = [];
    public ICollection<WebsiteAccessDB> WebsiteAccesses { get; set; } = [];
    public ICollection<SearchEngineAccessDB> SearchEngineAccesses { get; set; } = [];
    public ICollection<ContainerAccessDB> ContainerAccesses { get; set; } = [];
    public ICollection<AgentPermissionDB> AgentPermissions { get; set; } = [];
    public ICollection<TaskPermissionDB> TaskPermissions { get; set; } = [];
    public ICollection<SkillPermissionDB> SkillPermissions { get; set; } = [];

    // ── Clearance whitelists ──────────────────────────────────────

    /// <summary>
    /// Users who can approve agent actions at
    /// <see cref="PermissionClearance.ApprovedByWhitelistedUser"/>.
    /// </summary>
    public ICollection<ClearanceUserWhitelistEntryDB> ClearanceUserWhitelist { get; set; } = [];

    /// <summary>
    /// Agents who can approve other agents' actions at
    /// <see cref="PermissionClearance.ApprovedByWhitelistedAgent"/>.
    /// </summary>
    public ICollection<ClearanceAgentWhitelistEntryDB> ClearanceAgentWhitelist { get; set; } = [];
}
