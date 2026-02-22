using System.Text.Json.Serialization;
using SharpClaw.Contracts.DTOs.AgentActions;

namespace SharpClaw.Contracts.DTOs.Chat;

/// <summary>
/// A single event in a streamed chat session. The <see cref="Type"/>
/// discriminator determines which fields are populated.
/// </summary>
public sealed record ChatStreamEvent
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ChatStreamEventType Type { get; init; }

    /// <summary>Partial text token from the assistant. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.TextDelta"/>.</summary>
    public string? Delta { get; init; }

    /// <summary>Tool call being executed. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.ToolCallStart"/>.</summary>
    public AgentJobResponse? Job { get; init; }

    /// <summary>Tool call result. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.ToolCallResult"/>.</summary>
    public AgentJobResponse? Result { get; init; }

    /// <summary>Job awaiting approval. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.ApprovalRequired"/>.</summary>
    public AgentJobResponse? PendingJob { get; init; }

    /// <summary>Approval decision result. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.ApprovalResult"/>.</summary>
    public AgentJobResponse? ApprovalOutcome { get; init; }

    /// <summary>Error message. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.Error"/>.</summary>
    public string? Error { get; init; }

    /// <summary>Final persisted messages. Set when <see cref="Type"/> is <see cref="ChatStreamEventType.Done"/>.</summary>
    public ChatResponse? FinalResponse { get; init; }

    public static ChatStreamEvent TextDelta(string delta) =>
        new() { Type = ChatStreamEventType.TextDelta, Delta = delta };

    public static ChatStreamEvent ToolStart(AgentJobResponse job) =>
        new() { Type = ChatStreamEventType.ToolCallStart, Job = job };

    public static ChatStreamEvent ToolResult(AgentJobResponse result) =>
        new() { Type = ChatStreamEventType.ToolCallResult, Result = result };

    public static ChatStreamEvent NeedsApproval(AgentJobResponse job) =>
        new() { Type = ChatStreamEventType.ApprovalRequired, PendingJob = job };

    public static ChatStreamEvent ApprovalDecision(AgentJobResponse outcome) =>
        new() { Type = ChatStreamEventType.ApprovalResult, ApprovalOutcome = outcome };

    public static ChatStreamEvent Err(string message) =>
        new() { Type = ChatStreamEventType.Error, Error = message };

    public static ChatStreamEvent Complete(ChatResponse response) =>
        new() { Type = ChatStreamEventType.Done, FinalResponse = response };
}

public enum ChatStreamEventType
{
    /// <summary>Partial text token from the model.</summary>
    TextDelta,

    /// <summary>A tool call was detected and a job submitted.</summary>
    ToolCallStart,

    /// <summary>A tool call job completed (or failed/was denied).</summary>
    ToolCallResult,

    /// <summary>A job requires user approval before execution. The consumer
    /// must call the approval callback to continue.</summary>
    ApprovalRequired,

    /// <summary>Approval decision has been applied (approved → executed, or denied).</summary>
    ApprovalResult,

    /// <summary>An error occurred during the stream.</summary>
    Error,

    /// <summary>Stream complete. Contains the final persisted response.</summary>
    Done
}
