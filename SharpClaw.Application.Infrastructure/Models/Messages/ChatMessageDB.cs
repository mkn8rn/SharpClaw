using SharpClaw.Application.Infrastructure.Models.Context;
using SharpClaw.Contracts.Entities;

namespace SharpClaw.Application.Infrastructure.Models.Messages;

public class ChatMessageDB : BaseEntity
{
    public required string Role { get; set; }
    public required string Content { get; set; }

    public Guid ConversationId { get; set; }
    public ChannelDB Conversation { get; set; } = null!;
}
