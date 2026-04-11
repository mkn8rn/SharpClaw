namespace SharpClaw.Contracts.DTOs.Chat;

/// <summary>
/// Unified token usage summary — used for jobs, agents, and any
/// other context where a flat prompt/completion/total triple is needed.
/// </summary>
public sealed record TokenUsageResponse(
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTokens);

/// <summary>
/// Token usage breakdown for a single agent within a channel or thread.
/// </summary>
public sealed record AgentTokenBreakdown(
    Guid AgentId,
    string AgentName,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

/// <summary>
/// Aggregated token usage for a channel, with per-agent breakdown.
/// </summary>
public sealed record ChannelCostResponse(
    Guid ChannelId,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTokens,
    IReadOnlyList<AgentTokenBreakdown> AgentBreakdown);

/// <summary>
/// Aggregated token usage for a thread, with per-agent breakdown.
/// </summary>
public sealed record ThreadCostResponse(
    Guid ThreadId,
    Guid ChannelId,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTokens,
    IReadOnlyList<AgentTokenBreakdown> AgentBreakdown);

/// <summary>
/// Aggregated token usage across all channels for a single agent,
/// with per-channel breakdown.
/// </summary>
public sealed record AgentCostResponse(
    Guid AgentId,
    string AgentName,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTokens,
    IReadOnlyList<AgentChannelTokenBreakdown> ChannelBreakdown);

/// <summary>
/// Per-channel token usage for a single agent.
/// </summary>
public sealed record AgentChannelTokenBreakdown(
    Guid ChannelId,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);
