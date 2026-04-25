using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Infrastructure.Models;

public class ModelDB : BaseEntity
{
    public required string Name { get; set; }

    /// <summary>
    /// Advertised capabilities. Defaults to <see cref="ModelCapability.Chat"/>.
    /// </summary>
    public ModelCapability Capabilities { get; set; } = ModelCapability.Chat;

    public Guid ProviderId { get; set; }
    public ProviderDB Provider { get; set; } = null!;

    public ICollection<AgentDB> Agents { get; set; } = [];
}
