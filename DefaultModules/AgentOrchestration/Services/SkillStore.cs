using System.Text.Json;
using SharpClaw.Contracts.Modules;
using SharpClaw.Modules.AgentOrchestration.Models;

namespace SharpClaw.Modules.AgentOrchestration.Services;

public sealed class SkillStore
{
    private const string StorageName = "skills";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ModuleDocumentStore<SkillDB> _store;

    public SkillStore(IModuleStorageGateway storageGateway)
    {
        _store = new ModuleDocumentStore<SkillDB>(
            storageGateway,
            AgentOrchestrationModule.ModuleIdValue,
            StorageName,
            JsonOptions);
    }

    public async Task<SkillDB> CreateAsync(SkillDB skill, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (skill.Id == Guid.Empty)
            skill.Id = Guid.NewGuid();
        if (skill.CreatedAt == default)
            skill.CreatedAt = now;
        skill.UpdatedAt = now;
        await SaveAsync(skill, ct);
        return skill;
    }

    public Task<SkillDB?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _store.GetAsync(Key(id), ct);

    public async Task<IReadOnlyList<SkillDB>> ListAsync(CancellationToken ct = default) =>
        [.. (await _store.ListAsync(ct)).OrderBy(skill => skill.Name, StringComparer.Ordinal)];

    public async Task<SkillDB?> UpdateAsync(
        Guid id,
        Action<SkillDB> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var skill = await GetByIdAsync(id, ct);
        if (skill is null)
            return null;

        update(skill);
        skill.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(skill, ct);
        return skill;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        _store.DeleteAsync(Key(id), ct);

    private Task SaveAsync(SkillDB skill, CancellationToken ct) =>
        _store.UpsertAsync(
            Key(skill.Id),
            skill,
            new { name = skill.Name },
            ct);

    private static string Key(Guid id) => id.ToString("N");
}
