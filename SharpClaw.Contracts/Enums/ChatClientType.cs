namespace SharpClaw.Contracts.Enums;

/// <summary>
/// Identifies the client interface that originated a chat message.
/// Used in the chat header so the agent knows the communication channel.
/// </summary>
public enum ChatClientType
{
    CLI,
    API,
    Telegram,
    Discord,
    WhatsApp,
    Other
}
