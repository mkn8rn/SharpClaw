using Microsoft.EntityFrameworkCore;
using SharpClaw.Core.Conversation;
using SharpClaw.Contracts.Entities.Core.Context;
using SharpClaw.Contracts.DTOs.Threads;
using SharpClaw.Infrastructure.Persistence;

namespace SharpClaw.Application.Services;

public sealed class ThreadService(
    SharpClawDbContext db,
    ConversationTopologyEngine conversation,
    ConversationAdministrationEngine administration,
    EfConversationAdministrationHost administrationHost)
{
    public async Task<ThreadResponse> CreateAsync(
        Guid channelId, CreateThreadRequest request, CancellationToken ct = default)
    {
        return await administration.CreateThreadAsync(
            channelId,
            request,
            administrationHost,
            ct);
    }

    public async Task<ThreadResponse?> GetByIdAsync(
        Guid threadId, CancellationToken ct = default)
    {
        var thread = await db.ChatThreads.FindAsync([threadId], ct);
        return thread is not null ? conversation.ToThreadResponse(thread) : null;
    }

    public async Task<IReadOnlyList<ThreadResponse>> ListAsync(
        Guid channelId, CancellationToken ct = default)
    {
        var threads = await db.ChatThreads
            .Where(t => t.ChannelId == channelId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

        return threads.Select(conversation.ToThreadResponse).ToList();
    }

    public async Task<ThreadResponse?> UpdateAsync(
        Guid threadId, UpdateThreadRequest request, CancellationToken ct = default)
    {
        return await administration.UpdateThreadAsync(
            threadId,
            request,
            administrationHost,
            ct);
    }

    public async Task<bool> DeleteAsync(Guid threadId, CancellationToken ct = default)
    {
        return await administration.DeleteThreadAsync(
            threadId,
            administrationHost,
            ct);
    }

}
