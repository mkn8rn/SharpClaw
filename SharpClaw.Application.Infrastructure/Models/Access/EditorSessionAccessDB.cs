using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Resources;
using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Infrastructure.Models.Access;

/// <summary>
/// Grants an agent (via its role's permission set) access to an
/// <see cref="EditorSessionDB"/>.  All editor tool calls
/// (read file, apply edit, run build, etc.) check this grant.
/// </summary>
public class EditorSessionAccessDB : BaseEntity
{
    public PermissionClearance Clearance { get; set; }

    public Guid PermissionSetId { get; set; }
    public PermissionSetDB PermissionSet { get; set; } = null!;

    public Guid EditorSessionId { get; set; }
    public EditorSessionDB EditorSession { get; set; } = null!;
}
