using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharpClaw.Contracts.DTOs.Providers;
using SharpClaw.Contracts.Entities.Core;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Contracts.Providers;
using SharpClaw.Core.Clients;
using SharpClaw.Core.Providers;
using SharpClaw.Core.State;
using SharpClaw.Runtime.INF.Persistence;
using SharpClaw.Shared.Security;

namespace SharpClaw.Runtime.BLL.Services;

public sealed class EfProviderModelAdministrationHost(
    SharpClawDbContext db,
    EncryptionOptions encryptionOptions,
    ProviderApiClientFactory clientFactory,
    IConfiguration configuration) : IProviderModelAdministrationHost
{
    private readonly CoreStateSession _states = new(db);

    public bool UniqueProviderNamesEnforced =>
        ProviderCatalogEngine.IsUniqueNameEnforced(
            configuration["UniqueNames:Providers"]);

    public bool UniqueModelNamesEnforced =>
        ModelCatalogEngine.IsUniqueNameEnforced(
            configuration["UniqueNames:Models"]);

    public IEnumerable<IProviderPlugin> ProviderPlugins => clientFactory.Plugins;

    public IProviderPlugin? GetProviderPlugin(string providerKey)
    {
        return clientFactory.GetPlugin(providerKey);
    }

    public string ProtectProviderSecret(string secret)
    {
        return encryptionOptions.EncryptProviderKeys
            ? ApiKeyEncryptor.Encrypt(secret, encryptionOptions.Key)
            : secret;
    }

    public string UnprotectProviderSecret(string protectedSecret)
    {
        return ApiKeyEncryptor.DecryptOrPassthrough(
            protectedSecret,
            encryptionOptions.Key);
    }

    public async Task<ProviderState?> LoadProviderAsync(
        Guid providerId,
        CancellationToken ct)
    {
        var entity = await db.Providers.FindAsync([providerId], ct);
        return entity is null ? null : _states.Map(entity);
    }

    public async Task<ProviderState?> LoadProviderWithModelsAsync(
        Guid providerId,
        CancellationToken ct)
    {
        var entity = await db.Providers
            .Include(p => p.Models)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);
        return entity is null ? null : _states.Map(entity);
    }

    public async Task<ModelState?> LoadModelAsync(
        Guid modelId,
        CancellationToken ct)
    {
        var entity = await db.Models
            .Include(m => m.Provider)
            .FirstOrDefaultAsync(m => m.Id == modelId, ct);
        return entity is null ? null : _states.Map(entity);
    }

    public async Task<IReadOnlyList<ProviderState>> ListProvidersAsync(
        CancellationToken ct)
    {
        var entities = await db.Providers
            .OrderBy(provider => provider.Name)
            .ToListAsync(ct);
        return _states.Map(entities);
    }

    public async Task<IReadOnlyList<ModelState>> ListModelsAsync(
        Guid? providerId,
        CancellationToken ct)
    {
        var query = db.Models
            .Include(model => model.Provider)
            .AsQueryable();

        if (providerId is not null)
            query = query.Where(model => model.ProviderId == providerId);

        var entities = await query
            .OrderBy(model => model.Provider.Name)
            .ThenBy(model => model.Name)
            .ToListAsync(ct);
        return _states.Map(entities);
    }

    public async Task<IReadOnlyList<ModelState>> ListModelsForProviderAsync(
        Guid providerId,
        CancellationToken ct)
    {
        var entities = await db.Models
            .Where(m => m.ProviderId == providerId)
            .ToListAsync(ct);
        return _states.Map(entities);
    }

    public async Task<IReadOnlyList<string>> ListProviderNamesAsync(
        Guid? excludeId,
        CancellationToken ct)
    {
        return await db.Providers
            .Where(p => excludeId == null || p.Id != excludeId)
            .Select(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ModelNameExistsAsync(
        string name,
        Guid? excludeId,
        CancellationToken ct)
    {
        return await db.Models.AnyAsync(
            model => model.Name == name
                && (excludeId == null || model.Id != excludeId),
            ct);
    }

    public async Task<IReadOnlyList<string>> ListProviderModelIdsAsync(
        ProviderState provider,
        IProviderPlugin plugin,
        CancellationToken ct)
    {
        var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
            ? string.Empty
            : UnprotectProviderSecret(provider.EncryptedApiKey);
        var client = ProviderCredentialBinder.CreateClient(
            plugin,
            new ProviderClientOptions(provider.ApiEndpoint),
            apiKey);
        return await client.ListModelIdsAsync(ct);
    }

    public async Task<DeviceCodeSession> StartDeviceCodeFlowAsync(
        IDeviceCodeFlow deviceCodeFlow,
        CancellationToken ct)
    {
        return await deviceCodeFlow.StartAsync(ct);
    }

    public async Task<string?> PollDeviceCodeFlowAsync(
        IDeviceCodeFlow deviceCodeFlow,
        DeviceCodeSession session,
        CancellationToken ct)
    {
        return await deviceCodeFlow.PollAsync(session, ct);
    }

    public void TrackProvider(ProviderState provider)
    {
        _states.Track(provider);
    }

    public void TrackModel(ModelState model)
    {
        _states.Track(model);
    }

    public void TrackModels(IReadOnlyList<ModelState> models)
    {
        foreach (var model in models)
            _states.Track(model);
    }

    public void RemoveProvider(ProviderState provider)
    {
        _states.Remove(provider);
    }

    public void RemoveModel(ModelState model)
    {
        _states.Remove(model);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        await _states.SaveChangesAsync(ct);
    }
}
