using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.DatabaseAccess.Enums;

namespace SharpClaw.Modules.DatabaseAccess.Models;

/// <summary>Module-owned copy; SkillId stored as bare <see cref="Guid"/>.</summary>
public class ExternalDatabaseDB : BaseEntity
{
    public required string Name { get; set; }
    public DatabaseType DatabaseType { get; set; }
    public required string EncryptedConnectionString { get; set; }
    public string? Description { get; set; }
    public Guid? SkillId { get; set; }
}
