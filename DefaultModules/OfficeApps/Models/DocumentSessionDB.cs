using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Modules.OfficeApps.Models;

/// <summary>
/// A registered document file path that agents can operate on.
/// Module-owned copy; FK to SkillDB stored as bare <see cref="Guid"/>.
/// </summary>
public class DocumentSessionDB : BaseEntity
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DocumentType DocumentType { get; set; }
    public string? Description { get; set; }
}
