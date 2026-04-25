using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.WebAccess.Enums;

namespace SharpClaw.Modules.WebAccess.Models;

/// <summary>
/// A registered search engine that agents can query.
/// Module-owned copy; SkillId stored as bare <see cref="Guid"/>.
/// </summary>
public class SearchEngineDB : BaseEntity
{
    public required string Name { get; set; }
    public SearchEngineType Type { get; set; } = SearchEngineType.Custom;
    public required string Endpoint { get; set; }
    public string? Description { get; set; }
    public string? EncryptedApiKey { get; set; }
    public string? EncryptedSecondaryKey { get; set; }
    public Guid? SkillId { get; set; }
}
