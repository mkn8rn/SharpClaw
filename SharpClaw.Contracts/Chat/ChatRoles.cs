namespace SharpClaw.Contracts.Chat;

/// <summary>
/// Canonical role string literals used on the wire when talking to LLM
/// providers (OpenAI, Anthropic, Google, OpenRouter, Groq, Cerebras,
/// Mistral, xAI, Z.AI, Vercel, GitHub Copilot, Minimax, custom OpenAI-
/// compatible endpoints, …).
/// <para>
/// <b>Why these strings exist.</b> Every provider's chat / responses API
/// requires each message in the conversation history to carry a
/// <c>role</c> field whose value is one of <c>"user"</c>,
/// <c>"assistant"</c>, <c>"system"</c>, or <c>"tool"</c> (or
/// <c>"function"</c> for the legacy OpenAI Chat Completions tool
/// protocol, which we do not emit). The provider uses this field to
/// decide how the message is rendered into the model's prompt — e.g.
/// who is "speaking", which messages should be treated as instructions,
/// and which messages are tool-call results being fed back to the
/// model. The strings are not an internal SharpClaw concern; they are
/// part of the provider's public protocol and must be spelled exactly
/// as written here.
/// </para>
/// <para>
/// <b>SharpClaw's own notion of "who sent this message" is separate.</b>
/// See <see cref="SharpClaw.Contracts.Enums.MessageOrigin"/> and the
/// <c>Origin</c> column on the persisted chat message entity. The
/// provider <c>Role</c> stays on the wire; <c>Origin</c> is what
/// SharpClaw uses internally for routing, deduplication, and UI
/// presentation. The two intentionally do not have to agree (for
/// example, a SharpClaw-injected error notice persisted with
/// <c>Role = "system"</c> has <c>Origin = System</c>; a transcribed
/// audio prompt is persisted with <c>Role = "user"</c> but may carry
/// a more specific <c>Origin</c> in the future).
/// </para>
/// </summary>
public static class ChatRoles
{
    /// <summary>End-user authored message (provider role string <c>"user"</c>).</summary>
    public const string User = "user";

    /// <summary>Model-generated reply (provider role string <c>"assistant"</c>).</summary>
    public const string Assistant = "assistant";

    /// <summary>System / instruction message (provider role string <c>"system"</c>).</summary>
    public const string System = "system";

    /// <summary>Tool-call result fed back to the model (provider role string <c>"tool"</c>).</summary>
    public const string Tool = "tool";
}
