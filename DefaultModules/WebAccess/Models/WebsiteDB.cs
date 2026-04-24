using SharpClaw.Contracts.Entities;

namespace SharpClaw.Modules.WebAccess.Models;

/// <summary>
/// A registered website that agents can be granted access to.
/// Module-owned copy; SkillId stored as bare <see cref="Guid"/>.
/// </summary>
public class WebsiteDB : BaseEntity
{
    public required string Name { get; set; }
    public required string Url { get; set; }
    public string? Description { get; set; }
    public string? EncryptedCredentials { get; set; }
    public string? LoginUrl { get; set; }
    public Guid? SkillId { get; set; }
}
