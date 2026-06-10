using System.Text.Json;
using SharpClaw.Contracts.Modules;
using SharpClaw.Modules.AgentOrchestration.Models;
using SharpClaw.Modules.AgentOrchestration.ScheduledJobs;

namespace SharpClaw.Modules.AgentOrchestration.Services;

public sealed class ScheduledJobStore
{
    private const string StorageName = "scheduled_jobs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ModuleDocumentStore<ScheduledJobDB> _store;

    public ScheduledJobStore(IModuleStorageGateway storageGateway)
    {
        _store = new ModuleDocumentStore<ScheduledJobDB>(
            storageGateway,
            AgentOrchestrationModule.ModuleIdValue,
            StorageName,
            JsonOptions);
    }

    public async Task<ScheduledJobDB> CreateAsync(
        ScheduledJobDB job,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (job.Id == Guid.Empty)
            job.Id = Guid.NewGuid();
        if (job.CreatedAt == default)
            job.CreatedAt = now;
        job.UpdatedAt = now;
        await SaveAsync(job, ct);
        return job;
    }

    public Task<ScheduledJobDB?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _store.GetAsync(Key(id), ct);

    public async Task<IReadOnlyList<ScheduledJobDB>> ListAsync(CancellationToken ct = default) =>
        [.. (await _store.ListAsync(ct)).OrderBy(job => job.Name, StringComparer.Ordinal)];

    public async Task<IReadOnlyList<ScheduledJobDB>> ListDueAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var due = await _store.QueryAsync(
            "nextRunAt",
            lessThanOrEqual: now,
            order: "asc",
            ct: ct);
        return [.. due
            .Where(job => job.Status == ScheduledTaskStatus.Pending && job.NextRunAt <= now)
            .OrderBy(job => job.NextRunAt)];
    }

    public async Task<ScheduledJobDB?> UpdateAsync(
        Guid id,
        Action<ScheduledJobDB> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var job = await GetByIdAsync(id, ct);
        if (job is null)
            return null;

        update(job);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(job, ct);
        return job;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        _store.DeleteAsync(Key(id), ct);

    private Task SaveAsync(ScheduledJobDB job, CancellationToken ct) =>
        _store.UpsertAsync(
            Key(job.Id),
            job,
            new
            {
                name = job.Name,
                status = job.Status.ToString(),
                nextRunAt = job.NextRunAt,
            },
            ct);

    private static string Key(Guid id) => id.ToString("N");
}
