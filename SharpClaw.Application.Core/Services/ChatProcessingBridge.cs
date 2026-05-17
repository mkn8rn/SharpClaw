using SharpClaw.Contracts.Chat;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Providers;

namespace SharpClaw.Application.Services;

/// <summary>
/// Default <see cref="IChatProcessingBridge"/> implementation.
/// Fans out to every registered <see cref="IChatProcessingContributor"/>,
/// aggregates their contributions, and de-duplicates results so core
/// (<see cref="ChatService"/>, <see cref="HeaderTagProcessor"/>) never
/// names a module-owned permission key inline.
///
/// Modules contribute by registering their own
/// <see cref="IChatProcessingContributor"/> in DI; this aggregator picks
/// them up automatically through <c>IEnumerable&lt;IChatProcessingContributor&gt;</c>.
/// </summary>
public sealed class ChatProcessingBridge(
    IEnumerable<IChatProcessingContributor> contributors,
    ChatCache chatCache) : IChatProcessingBridge
{
    public async Task<IReadOnlyList<ChatToolDefinition>> GetExtraToolsAsync(
        Guid agentId, CancellationToken ct = default)
        => await chatCache.GetOrCreateAsync(
            ChatCache.KeyExtraTools(agentId),
            async innerCt => await ResolveExtraToolsAsync(agentId, innerCt),
            EstimateTools,
            ct) ?? [];

    public async Task<IReadOnlyList<ThreadSummary>> GetAccessibleThreadsAsync(
        Guid agentId, Guid currentChannelId, CancellationToken ct = default)
        => await chatCache.GetOrCreateAsync(
            ChatCache.KeyAccessibleThreads(agentId, currentChannelId),
            async innerCt => await ResolveAccessibleThreadsAsync(agentId, currentChannelId, innerCt),
            EstimateThreads,
            ct) ?? [];

    private async Task<IReadOnlyList<ChatToolDefinition>> ResolveExtraToolsAsync(
        Guid agentId, CancellationToken ct)
    {
        var aggregated = new List<ChatToolDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contributor in contributors)
        {
            var tools = await contributor.GetExtraToolsAsync(agentId, ct);
            foreach (var tool in tools)
            {
                if (seen.Add(tool.Name))
                    aggregated.Add(tool);
            }
        }
        return aggregated;
    }

    private async Task<IReadOnlyList<ThreadSummary>> ResolveAccessibleThreadsAsync(
        Guid agentId, Guid currentChannelId, CancellationToken ct)
    {
        var aggregated = new List<ThreadSummary>();
        var seen = new HashSet<Guid>();
        foreach (var contributor in contributors)
        {
            var threads = await contributor.GetAccessibleThreadsAsync(
                agentId, currentChannelId, ct);
            foreach (var t in threads)
            {
                if (seen.Add(t.ThreadId))
                    aggregated.Add(t);
            }
        }
        return aggregated;
    }

    private static long EstimateTools(IReadOnlyList<ChatToolDefinition> tools)
        => 128 + tools.Sum(static tool =>
            96
            + ChatCache.EstimateString(tool.Name)
            + ChatCache.EstimateString(tool.Description)
            + ChatCache.EstimateString(tool.ParametersSchema.GetRawText()));

    private static long EstimateThreads(IReadOnlyList<ThreadSummary> threads)
        => 128 + threads.Sum(static thread =>
            96
            + ChatCache.EstimateString(thread.ThreadName)
            + ChatCache.EstimateString(thread.ChannelTitle));
}
