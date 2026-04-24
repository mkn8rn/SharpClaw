namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Thread info for cross-thread history access.
/// </summary>
public sealed record ThreadSummary(
    Guid ThreadId,
    string ThreadName,
    Guid ChannelId,
    string ChannelTitle);

/// <summary>
/// A single chat message for history reads.
/// </summary>
public sealed record ChatMessageSummary(
    string Role,
    string Content,
    string Sender,
    DateTimeOffset Timestamp);

/// <summary>
/// Host-side read service for the ContextTools inline tools.
/// Provides access to chat threads, messages, and cross-thread history
/// without exposing core Infrastructure entities to modules.
/// </summary>
public interface IContextDataReader
{
    /// <summary>Returns true if the thread exists and belongs to the given channel.</summary>
    Task<bool> ThreadExistsAsync(Guid threadId, Guid channelId, CancellationToken ct = default);

    /// <summary>Returns messages in the thread, oldest first, limited to <paramref name="maxMessages"/>.</summary>
    Task<IReadOnlyList<ChatMessageSummary>> GetThreadMessagesAsync(
        Guid threadId, int maxMessages, CancellationToken ct = default);

    /// <summary>
    /// Returns threads accessible to the agent from channels other than
    /// <paramref name="currentChannelId"/>, respecting cross-thread history
    /// permissions and channel opt-in flags.
    /// </summary>
    Task<IReadOnlyList<ThreadSummary>> GetAccessibleThreadsAsync(
        Guid agentId, Guid currentChannelId, CancellationToken ct = default);
}
