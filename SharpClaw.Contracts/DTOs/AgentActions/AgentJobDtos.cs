using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.DTOs.AgentActions;

// ── Requests ──────────────────────────────────────────────────────

public sealed record SubmitAgentJobRequest(
string? ActionKey = null,
Guid? ResourceId = null,
Guid? AgentId = null,
Guid? CallerAgentId = null,
string? ScriptJson = null,
string? WorkingDirectory = null);

public sealed record ApproveAgentJobRequest(
    Guid? ApproverAgentId = null);

// ── Responses ─────────────────────────────────────────────────────

public sealed record AgentJobResponse(
Guid Id,
Guid ChannelId,
Guid AgentId,
string? ActionKey,
Guid? ResourceId,
AgentJobStatus Status,
PermissionClearance EffectiveClearance,
string? ResultData,
string? ErrorLog,
IReadOnlyList<AgentJobLogResponse> Logs,
DateTimeOffset CreatedAt,
DateTimeOffset? StartedAt,
DateTimeOffset? CompletedAt,
string? ScriptJson = null,
string? WorkingDirectory = null,
TokenUsageResponse? JobCost = null,
ChannelCostResponse? ChannelCost = null);

public sealed record AgentJobLogResponse(
    string Message,
    string Level,
    DateTimeOffset Timestamp);

/// <summary>
/// Lightweight summary returned by the list-summaries endpoint.
/// Contains only the fields needed to populate a dropdown or list view —
/// no <c>ResultData</c>, <c>ErrorLog</c>, <c>Logs</c>, or <c>Segments</c>.
/// </summary>
public sealed record AgentJobSummaryResponse(
    Guid Id,
    Guid ChannelId,
    Guid AgentId,
    string? ActionKey,
    Guid? ResourceId,
    AgentJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
