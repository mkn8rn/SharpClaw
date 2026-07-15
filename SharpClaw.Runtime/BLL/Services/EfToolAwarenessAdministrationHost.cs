using Microsoft.EntityFrameworkCore;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Core.Chat;
using SharpClaw.Core.Tools;
using SharpClaw.Core.State;
using SharpClaw.Runtime.INF.Persistence;

namespace SharpClaw.Runtime.BLL.Services;

public sealed class EfToolAwarenessAdministrationHost(
    SharpClawDbContext db,
    ChatCache chatCache) : IToolAwarenessAdministrationHost
{
    private readonly CoreStateSession _states = new(db);

    public async Task<ToolAwarenessSetState?> LoadToolAwarenessSetAsync(
        Guid id,
        CancellationToken ct)
    {
        var entity = await db.ToolAwarenessSets.FindAsync([id], ct);
        return entity is null ? null : _states.Map(entity);
    }

    public async Task<IReadOnlyList<ToolAwarenessSetState>> ListToolAwarenessSetsAsync(
        CancellationToken ct)
    {
        var entities = await db.ToolAwarenessSets
            .OrderBy(set => set.Name)
            .ToListAsync(ct);
        return _states.Map(entities);
    }

    public void TrackToolAwarenessSet(ToolAwarenessSetState entity)
    {
        _states.Track(entity);
    }

    public void RemoveToolAwarenessSet(ToolAwarenessSetState entity)
    {
        _states.Remove(entity);
    }

    public async Task SaveAsync(
        Func<ChatRuntimeInvalidationPlan?>? buildInvalidationPlan,
        CancellationToken ct)
    {
        await _states.SaveChangesAsync(ct);
        buildInvalidationPlan?.Invoke()?.ApplyTo(chatCache);
    }
}
