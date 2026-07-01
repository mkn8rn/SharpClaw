using Microsoft.EntityFrameworkCore;
using SharpClaw.Core.Conversation;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.DTOs.Channels;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class ChannelService(
    SharpClawDbContext db,
    ConversationTopologyEngine conversation,
    ConversationAdministrationEngine administration,
    EfConversationAdministrationHost administrationHost)
{
    /// <summary>
    /// Creates a new channel.  Either <see cref="CreateChannelRequest.AgentId"/>
    /// or <see cref="CreateChannelRequest.ContextId"/> (whose context has an
    /// agent) must be provided so the channel has a resolvable agent.
    /// </summary>
    public async Task<ChannelResponse> CreateAsync(
        CreateChannelRequest request, CancellationToken ct = default)
    {
        return await administration.CreateChannelAsync(
            request,
            administrationHost,
            ct);
    }

    public async Task<ChannelResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(id, ct);
        if (channel is null) return null;

        return conversation.ToChannelResponse(channel);
    }

    /// <summary>
    /// Lists channels, optionally filtered by agent or context.
    /// </summary>
    public async Task<IReadOnlyList<ChannelResponse>> ListAsync(
        Guid? agentId = null, Guid? contextId = null, CancellationToken ct = default)
    {
        var query = db.Channels
            .Include(c => c.Agent).ThenInclude(a => a!.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent).ThenInclude(a => a!.Role)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Role)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Role)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Role)
            .AsQueryable();

        if (agentId is not null)
            query = query.Where(c => c.AgentId == agentId);

        if (contextId is not null)
            query = query.Where(c => c.AgentContextId == contextId);

        var channels = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        return channels
            .Select(conversation.ToChannelResponse)
            .ToList();
    }

    public async Task<ChannelResponse?> UpdateAsync(
        Guid id, UpdateChannelRequest request, CancellationToken ct = default)
    {
        return await administration.UpdateChannelAsync(
            id,
            request,
            administrationHost,
            ct);
    }

    /// <summary>
    /// Returns the channel with the most recent chat message across all
    /// agents.  Falls back to the most recently created channel when no
    /// messages exist.  Used by the CLI when no channel is explicitly
    /// selected.
    /// </summary>
    public async Task<ChannelResponse?> GetLatestActiveAsync(CancellationToken ct = default)
    {
        // Find the channel that received the most recent message.
        var latestChannelId = await db.ChatMessages
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (Guid?)m.ChannelId)
            .FirstOrDefaultAsync(ct);

        ChannelDB? channel;
        if (latestChannelId is not null)
        {
            channel = await LoadChannelAsync(latestChannelId.Value, ct);
        }
        else
        {
            // No messages in any channel — fall back to most recently created.
            channel = await db.Channels
                .Include(c => c.Agent).ThenInclude(a => a!.Model).ThenInclude(m => m.Provider)
                .Include(c => c.Agent).ThenInclude(a => a!.Role)
                .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
                .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Role)
                .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
                .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Role)
                .Include(c => c.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
                .Include(c => c.AllowedAgents).ThenInclude(a => a.Role)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (channel is null) return null;
        return conversation.ToChannelResponse(channel);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await administration.DeleteChannelAsync(
            id,
            administrationHost,
            ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Granular operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the default agent for a channel.
    /// </summary>
    public async Task<ChannelResponse?> SetAgentAsync(
        Guid channelId, Guid agentId, CancellationToken ct = default)
    {
        return await administration.SetChannelAgentAsync(
            channelId,
            agentId,
            administrationHost,
            ct);
    }

    /// <summary>
    /// Lists the allowed agents for a channel (effective: channel's own,
    /// falling back to context's).
    /// </summary>
    public async Task<ChannelAllowedAgentsResponse?> ListAllowedAgentsAsync(
        Guid channelId, CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(channelId, ct);
        if (channel is null) return null;

        return conversation.ToChannelAllowedAgentsResponse(channel);
    }

    /// <summary>
    /// Adds an agent to the channel's allowed agents.
    /// </summary>
    public async Task<ChannelAllowedAgentsResponse?> AddAllowedAgentAsync(
        Guid channelId, Guid agentId, CancellationToken ct = default)
    {
        return await administration.AddChannelAllowedAgentAsync(
            channelId,
            agentId,
            administrationHost,
            ct);
    }

    /// <summary>
    /// Removes an agent from the channel's allowed agents.
    /// </summary>
    public async Task<ChannelAllowedAgentsResponse?> RemoveAllowedAgentAsync(
        Guid channelId, Guid agentId, CancellationToken ct = default)
    {
        return await administration.RemoveChannelAllowedAgentAsync(
            channelId,
            agentId,
            administrationHost,
            ct);
    }

    // ── Private helpers ───────────────────────────────────────────

    private async Task<ChannelDB?> LoadChannelAsync(Guid id, CancellationToken ct) =>
        await db.Channels
            .Include(c => c.Agent).ThenInclude(a => a!.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent).ThenInclude(a => a!.Role)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.Agent).ThenInclude(a => a.Role)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AgentContext).ThenInclude(ctx => ctx!.AllowedAgents).ThenInclude(a => a.Role)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Role)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

}
