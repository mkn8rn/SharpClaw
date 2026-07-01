using Microsoft.EntityFrameworkCore;
using SharpClaw.Core.Conversation;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.DTOs.Contexts;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class ContextService(
    SharpClawDbContext db,
    ConversationTopologyEngine conversation,
    ConversationAdministrationEngine administration,
    EfConversationAdministrationHost administrationHost)
{
    public async Task<ContextResponse> CreateAsync(
        CreateContextRequest request, CancellationToken ct = default)
    {
        return await administration.CreateContextAsync(
            request,
            administrationHost,
            ct);
    }

    public async Task<ContextResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var context = await LoadContextAsync(id, ct);
        return context is null ? null : conversation.ToContextResponse(context);
    }

    public async Task<IReadOnlyList<ContextResponse>> ListAsync(
        Guid? agentId = null, CancellationToken ct = default)
    {
        var query = db.AgentContexts
            .Include(c => c.Agent).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent).ThenInclude(a => a.Role)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Role)
            .AsQueryable();

        if (agentId is not null)
            query = query.Where(c => c.AgentId == agentId);

        var contexts = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        return contexts.Select(conversation.ToContextResponse).ToList();
    }

    public async Task<ContextResponse?> UpdateAsync(
        Guid id, UpdateContextRequest request, CancellationToken ct = default)
    {
        return await administration.UpdateContextAsync(
            id,
            request,
            administrationHost,
            ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return await administration.DeleteContextAsync(
            id,
            administrationHost,
            ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Granular operations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Lists the allowed agents for a context.
    /// </summary>
    public async Task<ContextAllowedAgentsResponse?> ListAllowedAgentsAsync(
        Guid contextId, CancellationToken ct = default)
    {
        var context = await LoadContextAsync(contextId, ct);
        if (context is null) return null;

        return conversation.ToContextAllowedAgentsResponse(context);
    }

    /// <summary>
    /// Adds an agent to the context's allowed agents.
    /// </summary>
    public async Task<ContextAllowedAgentsResponse?> AddAllowedAgentAsync(
        Guid contextId, Guid agentId, CancellationToken ct = default)
    {
        return await administration.AddContextAllowedAgentAsync(
            contextId,
            agentId,
            administrationHost,
            ct);
    }

    /// <summary>
    /// Removes an agent from the context's allowed agents.
    /// </summary>
    public async Task<ContextAllowedAgentsResponse?> RemoveAllowedAgentAsync(
        Guid contextId, Guid agentId, CancellationToken ct = default)
    {
        return await administration.RemoveContextAllowedAgentAsync(
            contextId,
            agentId,
            administrationHost,
            ct);
    }

    private async Task<ChannelContextDB?> LoadContextAsync(Guid id, CancellationToken ct) =>
        await db.AgentContexts
            .Include(c => c.Agent).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent).ThenInclude(a => a.Role)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Model).ThenInclude(m => m.Provider)
            .Include(c => c.AllowedAgents).ThenInclude(a => a.Role)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

}
