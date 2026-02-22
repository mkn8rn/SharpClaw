using System.Text.Json;

namespace SharpClaw.Application.Core.Clients;

/// <summary>
/// Describes a tool the model may invoke during a chat completion.
/// </summary>
public sealed record ChatToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema);

/// <summary>
/// A tool invocation emitted by the model in a chat completion response.
/// </summary>
public sealed record ChatToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

/// <summary>
/// The result of a tool-aware chat completion. Contains either text
/// content, one or more tool calls, or both (some providers return
/// partial text alongside tool invocations).
/// </summary>
public sealed class ChatCompletionResult
{
    public string? Content { get; init; }
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = [];
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// A single chunk from a streaming chat completion. Either a text delta
/// or a final result containing accumulated tool calls.
/// </summary>
public sealed class ChatStreamChunk
{
    /// <summary>Partial text token. <see langword="null"/> on the final chunk.</summary>
    public string? Delta { get; init; }

    /// <summary>
    /// Set on the final chunk only. Contains the complete list of tool
    /// calls (if any) and the full accumulated content.
    /// </summary>
    public ChatCompletionResult? Finished { get; init; }

    public bool IsFinished => Finished is not null;

    public static ChatStreamChunk Text(string delta) => new() { Delta = delta };

    public static ChatStreamChunk Final(ChatCompletionResult result) =>
        new() { Finished = result };
}

/// <summary>
/// A message in a tool-aware conversation history. Represents system,
/// user, assistant (with optional tool calls), and tool-result messages.
/// </summary>
public sealed record ToolAwareMessage
{
    public required string Role { get; init; }
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls emitted by the assistant. Present only when
    /// <see cref="Role"/> is <c>"assistant"</c>.
    /// </summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// The ID of the tool call this message responds to. Present
    /// only when <see cref="Role"/> is <c>"tool"</c>.
    /// </summary>
    public string? ToolCallId { get; init; }

    public static ToolAwareMessage System(string content) =>
        new() { Role = "system", Content = content };

    public static ToolAwareMessage User(string content) =>
        new() { Role = "user", Content = content };

    public static ToolAwareMessage Assistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ToolAwareMessage AssistantWithToolCalls(
        IReadOnlyList<ChatToolCall> toolCalls, string? content = null) =>
        new() { Role = "assistant", Content = content, ToolCalls = toolCalls };

    public static ToolAwareMessage ToolResult(string toolCallId, string content) =>
        new() { Role = "tool", Content = content, ToolCallId = toolCallId };
}
