using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Infrastructure.Models.Context;
using SharpClaw.Contracts.DTOs.Channels;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class ChannelService(SharpClawDbContext db)
{
    /// <summary>
    /// Creates a new channel for the given agent.  If no model is
    /// specified the agent's current model is used.  An optional
    /// <see cref="ChannelContextDB"/> can be linked so that the context's
    /// permission set acts as a default.
    /// </summary>
    public async Task<ChannelResponse> CreateAsync(
        CreateChannelRequest request, CancellationToken ct = default)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Id == request.AgentId, ct)
            ?? throw new ArgumentException($"Agent {request.AgentId} not found.");

        var modelId = request.ModelId ?? agent.ModelId;
        var model = modelId == agent.ModelId
            ? agent.Model
            : await db.Models.Include(m => m.Provider)
                  .FirstOrDefaultAsync(m => m.Id == modelId, ct)
              ?? throw new ArgumentException($"Model {modelId} not found.");

        ChannelContextDB? context = null;
        if (request.ContextId is { } ctxId)
        {
            context = await db.AgentContexts
                .FirstOrDefaultAsync(c => c.Id == ctxId, ct)
                ?? throw new ArgumentException($"Context {ctxId} not found.");
        }

        var channel = new ChannelDB
        {
            Title = request.Title ?? $"Channel {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
            ModelId = model.Id,
            AgentId = agent.Id,
            AgentContextId = context?.Id,
            PermissionSetId = request.PermissionSetId
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);

        return ToResponse(channel, agent, model, context);
    }

    public async Task<ChannelResponse?> GetByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(id, ct);
        if (channel is null) return null;

        return ToResponse(channel, channel.Agent, channel.Model, channel.AgentContext);
    }

    /// <summary>
    /// Lists channels, optionally filtered by agent or context.
    /// </summary>
    public async Task<IReadOnlyList<ChannelResponse>> ListAsync(
        Guid? agentId = null, Guid? contextId = null, CancellationToken ct = default)
    {
        var query = db.Channels
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .Include(c => c.AgentContext)
            .AsQueryable();

        if (agentId is not null)
            query = query.Where(c => c.AgentId == agentId);

        if (contextId is not null)
            query = query.Where(c => c.AgentContextId == contextId);

        var channels = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        return channels
            .Select(c => ToResponse(c, c.Agent, c.Model, c.AgentContext))
            .ToList();
    }

    public async Task<ChannelResponse?> UpdateAsync(
        Guid id, UpdateChannelRequest request, CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(id, ct);
        if (channel is null) return null;

        if (request.Title is not null)
            channel.Title = request.Title;

        if (request.ModelId is { } modelId)
        {
            var model = await db.Models.Include(m => m.Provider)
                .FirstOrDefaultAsync(m => m.Id == modelId, ct)
                ?? throw new ArgumentException($"Model {modelId} not found.");
            channel.ModelId = model.Id;
            channel.Model = model;
        }

        // Allow moving into / out of a context
        if (request.ContextId is not null)
        {
            if (request.ContextId == Guid.Empty)
            {
                channel.AgentContextId = null;
                channel.AgentContext = null;
            }
            else
            {
                var ctx = await db.AgentContexts
                    .FirstOrDefaultAsync(c => c.Id == request.ContextId, ct)
                    ?? throw new ArgumentException($"Context {request.ContextId} not found.");
                channel.AgentContextId = ctx.Id;
                channel.AgentContext = ctx;
            }
        }

        // Allow explicit set/unset of permission set
        if (request.PermissionSetId is not null)
            channel.PermissionSetId = request.PermissionSetId == Guid.Empty
                ? null
                : request.PermissionSetId;

        await db.SaveChangesAsync(ct);
        return ToResponse(channel, channel.Agent, channel.Model, channel.AgentContext);
    }

    /// <summary>
    /// Returns the channel with the most recent chat message across all
    /// agents.  Used by the CLI when no channel is explicitly selected.
    /// </summary>
    public async Task<ChannelResponse?> GetLatestActiveAsync(CancellationToken ct = default)
    {
        var channel = await db.Channels
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .Include(c => c.AgentContext)
            .Include(c => c.ChatMessages)
            .OrderByDescending(c => c.ChatMessages.Max(m => (DateTimeOffset?)m.CreatedAt) ?? c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (channel is null) return null;
        return ToResponse(channel, channel.Agent, channel.Model, channel.AgentContext);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var channel = await db.Channels.FindAsync([id], ct);
        if (channel is null) return false;

        db.Channels.Remove(channel);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Private helpers ───────────────────────────────────────────

    private async Task<ChannelDB?> LoadChannelAsync(Guid id, CancellationToken ct) =>
        await db.Channels
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .Include(c => c.AgentContext)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private static ChannelResponse ToResponse(
        ChannelDB channel, AgentDB agent, ModelDB model, ChannelContextDB? context) =>
        new(channel.Id,
            channel.Title,
            agent.Id,
            agent.Name,
            model.Id,
            model.Name,
            context?.Id,
            context?.Name,
            channel.PermissionSetId,
            channel.PermissionSetId ?? context?.PermissionSetId,
            channel.CreatedAt,
            channel.UpdatedAt);
}
