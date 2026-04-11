namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Context provided to inline tool execution. Unlike <see cref="AgentJobContext"/>
/// (which wraps a full job record), this provides only the minimal context needed
/// for inline tools: the agent, channel, and thread identifiers.
/// </summary>
public sealed record InlineToolContext(
    /// <summary>The agent executing this tool call.</summary>
    Guid AgentId,

    /// <summary>The channel the chat is occurring in.</summary>
    Guid ChannelId,

    /// <summary>The thread within the channel (if any).</summary>
    Guid? ThreadId,

    /// <summary>The tool call ID from the model response.</summary>
    string ToolCallId
);
