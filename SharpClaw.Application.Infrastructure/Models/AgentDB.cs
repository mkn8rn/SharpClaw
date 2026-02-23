using SharpClaw.Application.Infrastructure.Models.Clearance;
using SharpClaw.Application.Infrastructure.Models.Context;
using SharpClaw.Contracts.Entities;

namespace SharpClaw.Infrastructure.Models;

public class AgentDB : BaseEntity
{
    public required string Name { get; set; }
    public string? SystemPrompt { get; set; }

    public Guid ModelId { get; set; }
    public ModelDB Model { get; set; } = null!;

    public Guid? RoleId { get; set; }
    public RoleDB? Role { get; set; }

    public ICollection<ChannelContextDB> Contexts { get; set; } = [];
    public ICollection<ChannelDB> Channels { get; set; } = [];
}
