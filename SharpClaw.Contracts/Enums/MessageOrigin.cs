namespace SharpClaw.Contracts.Enums;

/// <summary>
/// SharpClaw's own classification of who produced a chat message,
/// independent of the provider-facing role string on the wire.
/// <para>
/// This is what SharpClaw uses internally — for deduplication, history
/// rendering, audit, and "is this row from a real user" checks — so the
/// code does not have to grep on the stringly-typed provider
/// <see cref="SharpClaw.Contracts.Chat.ChatRoles"/> values.
/// </para>
/// <para>
/// Stored as a string per the project-wide enum-as-string convention.
/// Nullable on the persisted entity: messages written before this field
/// existed have <c>null</c> here and callers should fall back to the
/// provider role string for those legacy rows.
/// </para>
/// </summary>
public enum MessageOrigin
{
    /// <summary>An authenticated end user (or external bridge user) sent the message.</summary>
    User,

    /// <summary>An agent (LLM) produced this message.</summary>
    Assistant,

    /// <summary>SharpClaw itself injected this message (errors, notices, system prompts).</summary>
    System,

    /// <summary>A tool-call result that was fed back to the model.</summary>
    Tool,
}
