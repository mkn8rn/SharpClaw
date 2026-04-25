using SharpClaw.Contracts.Attributes;
using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.BotIntegration.Contracts;

namespace SharpClaw.Modules.BotIntegration.Models;

/// <summary>
/// Stores a bot integration configuration (Telegram, Discord, etc.).
/// Module-owned copy of the host entity.
/// </summary>
public class BotIntegrationDB : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public required BotType BotType { get; set; }
    public bool Enabled { get; set; }

    [HeaderSensitive]
    public string? EncryptedBotToken { get; set; }

    public Guid? DefaultChannelId { get; set; }
    public Guid? DefaultThreadId { get; set; }

    [HeaderSensitive]
    public string? PlatformConfig { get; set; }
}
