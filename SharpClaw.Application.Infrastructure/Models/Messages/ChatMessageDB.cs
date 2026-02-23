using SharpClaw.Application.Infrastructure.Models.Context;
using SharpClaw.Contracts.Entities;

namespace SharpClaw.Application.Infrastructure.Models.Messages;

public class ChatMessageDB : BaseEntity
{
    public required string Role { get; set; }
    public required string Content { get; set; }

    public Guid ChannelId { get; set; }
    public ChannelDB Channel { get; set; } = null!;
}
