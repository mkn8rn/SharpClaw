using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SharpClaw.Contracts.Enums;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Modules.ContextTools.Services;

/// <summary>
/// Wraps cross-thread context and utility DB operations for the
/// Context Tools module (inline tools).
/// </summary>
internal sealed class ContextToolsService(SharpClawDbContext db)
{
    // ═══════════════════════════════════════════════════════════════
    // WAIT
    // ═══════════════════════════════════════════════════════════════

    public static async Task<string> WaitAsync(
        JsonElement parameters, CancellationToken ct)
    {
        var seconds = 5; // default

        if (parameters.TryGetProperty("seconds", out var secEl)
            && secEl.TryGetInt32(out var s))
            seconds = s;

        seconds = Math.Clamp(seconds, 1, 300);

        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

        return $"Waited {seconds} second{(seconds == 1 ? "" : "s")}.";
    }

    // ═══════════════════════════════════════════════════════════════
    // LIST ACCESSIBLE THREADS
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ListAccessibleThreadsAsync(
        Guid agentId, Guid channelId, CancellationToken ct)
    {
        var threads = await GetAccessibleThreadsAsync(agentId, channelId, ct);
        if (threads.Count == 0)
            return "No accessible threads found. Either the agent lacks the ReadCrossThreadHistory permission, or no other channels have opted in.";

        var result = threads.Select(t => new
        {
            threadId = t.ThreadId.ToString("D"),
            threadName = t.ThreadName,
            channelId = t.ChannelId.ToString("D"),
            channelTitle = t.ChannelTitle,
        });

        return JsonSerializer.Serialize(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // READ THREAD HISTORY
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ReadThreadHistoryAsync(
        JsonElement parameters, Guid agentId, Guid channelId, CancellationToken ct)
    {
        Guid threadId = Guid.Empty;
        int maxMessages = 50;

        if (parameters.TryGetProperty("threadId", out var tidEl))
            Guid.TryParse(tidEl.GetString(), out threadId);
        if (parameters.TryGetProperty("maxMessages", out var maxEl)
            && maxEl.TryGetInt32(out var mm))
            maxMessages = Math.Clamp(mm, 1, 200);

        if (threadId == Guid.Empty)
            return "Error: threadId is required.";

        // Load thread with its channel
        var thread = await db.ChatThreads
            .Include(t => t.Channel)
                .ThenInclude(c => c.AllowedAgents)
            .Include(t => t.Channel)
                .ThenInclude(c => c.PermissionSet)
            .Include(t => t.Channel)
                .ThenInclude(c => c.AgentContext)
                    .ThenInclude(ctx => ctx!.PermissionSet)
            .FirstOrDefaultAsync(t => t.Id == threadId, ct);

        if (thread is null)
            return "Error: thread not found.";

        // Must not be the current channel (use normal history for that)
        if (thread.ChannelId == channelId)
            return "Error: use normal chat history to access threads in the current channel.";

        // Check agent has access to the target channel
        var targetChannel = thread.Channel;
        var isAgentOnChannel = targetChannel.AgentId == agentId
            || targetChannel.AllowedAgents.Any(a => a.Id == agentId);
        if (!isAgentOnChannel)
            return "Error: agent is not assigned to the target channel.";

        // Check agent has ReadCrossThreadHistory permission
        var agentWithRole = await db.Agents
            .Include(a => a.Role)
                .ThenInclude(r => r!.PermissionSet)
            .FirstOrDefaultAsync(a => a.Id == agentId, ct);

        var agentPs = agentWithRole?.Role?.PermissionSet;
        if (agentPs is not { CanReadCrossThreadHistory: true })
            return "Error: agent lacks ReadCrossThreadHistory permission.";

        // Check channel opt-in (unless Independent clearance)
        if (agentPs.ReadCrossThreadHistoryClearance != PermissionClearance.Independent)
        {
            var effectivePs = targetChannel.PermissionSet
                ?? targetChannel.AgentContext?.PermissionSet;
            if (effectivePs?.CanReadCrossThreadHistory != true)
                return "Error: the target channel has not opted in to cross-thread history sharing.";
        }

        // Fetch messages
        var messages = await db.ChatMessages
            .Where(m => m.ThreadId == threadId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                role = m.Role,
                content = m.Content,
                sender = m.SenderUsername ?? m.SenderAgentName ?? "unknown",
                timestamp = m.CreatedAt
            })
            .ToListAsync(ct);

        if (messages.Count == 0)
            return "Thread exists but has no messages.";

        return JsonSerializer.Serialize(messages);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper: find accessible threads
    // ═══════════════════════════════════════════════════════════════

    private async Task<List<(Guid ThreadId, string ThreadName, Guid ChannelId, string ChannelTitle)>>
        GetAccessibleThreadsAsync(Guid agentId, Guid currentChannelId, CancellationToken ct)
    {
        var agentWithRole = await db.Agents
            .Include(a => a.Role)
                .ThenInclude(r => r!.PermissionSet)
            .FirstOrDefaultAsync(a => a.Id == agentId, ct);

        if (agentWithRole?.Role?.PermissionSet is not { CanReadCrossThreadHistory: true } agentPs)
            return [];

        var isIndependent = agentPs.ReadCrossThreadHistoryClearance == PermissionClearance.Independent;

        // Channels where the agent is primary or allowed, excluding current
        var channels = await db.Channels
            .Include(c => c.AllowedAgents)
            .Include(c => c.PermissionSet)
            .Include(c => c.AgentContext)
                .ThenInclude(ctx => ctx!.PermissionSet)
            .Where(c => c.Id != currentChannelId)
            .Where(c => c.AgentId == agentId || c.AllowedAgents.Any(a => a.Id == agentId))
            .ToListAsync(ct);

        // Filter by channel opt-in unless Independent
        if (!isIndependent)
        {
            channels = channels
                .Where(c =>
                {
                    var effectivePs = c.PermissionSet ?? c.AgentContext?.PermissionSet;
                    return effectivePs?.CanReadCrossThreadHistory == true;
                })
                .ToList();
        }

        if (channels.Count == 0)
            return [];

        var channelIds = channels.Select(c => c.Id).ToList();
        var threads = await db.ChatThreads
            .Where(t => channelIds.Contains(t.ChannelId))
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new { t.Id, t.Name, t.ChannelId })
            .ToListAsync(ct);

        var channelTitles = channels.ToDictionary(c => c.Id, c => c.Title);

        return threads
            .Select(t => (t.Id, t.Name, t.ChannelId, channelTitles[t.ChannelId]))
            .ToList();
    }
}
