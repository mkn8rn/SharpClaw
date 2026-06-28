using Microsoft.EntityFrameworkCore;
using SharpClaw.Core.Clients;
using SharpClaw.Core.Providers;
using SharpClaw.Contracts.DTOs.Providers;
using SharpClaw.Contracts.Providers;
using SharpClaw.Contracts.Persistence;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Utils.Security;

namespace SharpClaw.Application.Services;

public sealed class ProviderCostService(
    SharpClawDbContext db,
    EncryptionOptions encryptionOptions,
    ProviderApiClientFactory clientFactory,
    IHttpClientFactory httpClientFactory,
    ProviderCostEngine providerCosts)
{
    /// <summary>
    /// Fetches cost data for a single provider. If the provider exposes a
    /// billing API (e.g. OpenAI) and the API key has sufficient privileges,
    /// real cost data is returned. Otherwise a response is returned with
    /// <see cref="ProviderCostResponse.CostApiSupported"/> = <see langword="false"/>
    /// and an explanatory note.
    /// </summary>
    public async Task<ProviderCostResponse?> GetCostAsync(
        Guid providerId, int days = 30,
        DateTimeOffset? startDate = null, DateTimeOffset? endDate = null,
        CancellationToken ct = default)
    {
        var provider = await db.Providers.FindAsync([providerId], ct);
        if (provider is null) return null;

        var (periodStart, periodEnd) = providerCosts.ResolvePeriod(days, startDate, endDate);

        var plugin = clientFactory.GetPlugin(provider.ProviderKey);
        var costFeed = plugin?.CostFeed;
        var isLocal = plugin is { RequiresApiKey: false };
        var costProvider = new ProviderCostProvider(
            provider.Id,
            provider.Name,
            provider.ProviderKey);

        if (costFeed is not null
            && (!plugin!.RequiresApiKey || !string.IsNullOrEmpty(provider.EncryptedApiKey)))
        {
            var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
                ? string.Empty
                : ApiKeyEncryptor.DecryptOrPassthrough(provider.EncryptedApiKey, encryptionOptions.Key);
            using var httpClient = httpClientFactory.CreateClient();

            var result = await costFeed.GetCostsAsync(httpClient, apiKey, periodStart, periodEnd, ct);
            if (result is not null)
                return providerCosts.CreateFeedResponse(
                    costProvider,
                    isLocal,
                    periodStart,
                    periodEnd,
                    result);

            // API returned null - key likely lacks the required billing permissions.
            // The plugin owns the provider-specific remediation message.
            return providerCosts.CreatePermissionDeniedResponse(
                costProvider,
                isLocal,
                periodStart,
                periodEnd,
                costFeed.PermissionDeniedNote);
        }

        // Provider does not implement a cost API.
        return providerCosts.CreateUnsupportedResponse(
            costProvider,
            isLocal,
            periodStart,
            periodEnd);
    }

    /// <summary>
    /// Fetches cost data for configured providers and aggregates the results
    /// into a total report.
    /// </summary>
    /// <param name="days">Number of days to look back (default 30).</param>
    /// <param name="startDate">Explicit period start (overrides <paramref name="days"/>).</param>
    /// <param name="endDate">Explicit period end.</param>
    /// <param name="includeAll">
    /// When <see langword="false"/> (default), only providers that have an API
    /// key configured are included. When <see langword="true"/>, all providers
    /// are included regardless of API key status.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ProviderCostTotalResponse> GetTotalCostAsync(
        int days = 30,
        DateTimeOffset? startDate = null, DateTimeOffset? endDate = null,
        bool includeAll = false,
        CancellationToken ct = default)
    {
        var (periodStart, periodEnd) = providerCosts.ResolvePeriod(days, startDate, endDate);

        var query = db.Providers.AsQueryable();
        if (!includeAll)
            query = query.Where(p => p.EncryptedApiKey != null && p.EncryptedApiKey != "");

        var providers = await query.ToListAsync(ct);
        var results = new List<ProviderCostResponse>(providers.Count);

        foreach (var provider in providers)
        {
            var cost = await GetCostAsync(provider.Id, days, startDate, endDate, ct);
            if (cost is not null)
                results.Add(cost);
        }

        return providerCosts.CreateTotalResponse(periodStart, periodEnd, results);
    }
}
