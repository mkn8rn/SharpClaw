namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side service for agent lifecycle operations invoked by modules.
/// </summary>
public interface IAgentManager
{
    /// <summary>Creates a sub-agent with the given name, model, and optional system prompt. Returns the new agent ID and a display string.</summary>
    Task<(Guid AgentId, string ModelName, string AgentName)> CreateSubAgentAsync(
        string name, Guid modelId, string? systemPrompt, CancellationToken ct = default);

    /// <summary>Updates name and/or system prompt on an existing agent.</summary>
    Task<string> UpdateAgentAsync(
        Guid agentId, string? name, string? systemPrompt, Guid? modelId, CancellationToken ct = default);

    /// <summary>Sets or clears the custom chat header on an agent.</summary>
    Task SetAgentHeaderAsync(Guid agentId, string? header, CancellationToken ct = default);

    /// <summary>Sets or clears the custom chat header on a channel.</summary>
    Task SetChannelHeaderAsync(Guid channelId, string? header, CancellationToken ct = default);
}
