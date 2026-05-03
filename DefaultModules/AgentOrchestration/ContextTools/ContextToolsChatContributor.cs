using SharpClaw.Contracts.Chat;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Modules.AgentOrchestration;

/// <summary>
/// Module-owned <see cref="IChatProcessingContributor"/> that surfaces
/// the context-tools cross-thread visibility policy to the host chat
/// pipeline. The permission key
/// (<see cref="ContextToolsPermissionKeys.CanReadCrossThreadHistory"/>)
/// and the underlying query both live in this module; core only invokes
/// <see cref="IChatProcessingBridge"/> and never names the wire string.
/// Rolled into agent-orchestration from the former
/// <c>sharpclaw_context_tools</c> module.
/// </summary>
internal sealed class ContextToolsChatContributor(ContextDataReader dataReader)
    : IChatProcessingContributor
{
    public Task<IReadOnlyList<ThreadSummary>> GetAccessibleThreadsAsync(
        Guid agentId, Guid currentChannelId, CancellationToken ct = default)
        => dataReader.GetAccessibleThreadsAsync(agentId, currentChannelId, ct);
}
