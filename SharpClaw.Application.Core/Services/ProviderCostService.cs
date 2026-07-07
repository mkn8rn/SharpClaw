using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Core.Clients;
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
    IHttpClientFactory httpClientFactory)
{
    /// <summary>
    /// Currency reported when no cost feed data is available (no API support,
    /// permission denied, or empty totals). USD is used because every provider
    /// plugin that currently exposes a cost feed reports in USD; the value is
    /// only ever paired with a zero <c>TotalCost</c> in fallback responses.
    /// </summary>
    private const string DefaultFallbackCurrency = "usd";

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

        var (periodStart, periodEnd) = ResolvePeriod(days, startDate, endDate);

        var plugin = clientFactory.GetPlugin(provider.ProviderKey);
        var providerOptions = new ProviderClientOptions(provider.ApiEndpoint);
        var costFeed = plugin?.CreateCostFeed(providerOptions);
        var isLocal = plugin is { RequiresApiKey: false };

        if (costFeed is not null
            && (!plugin!.RequiresApiKey || !string.IsNullOrEmpty(provider.EncryptedApiKey)))
        {
            var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
                ? string.Empty
                : ApiKeyEncryptor.DecryptOrPassthrough(provider.EncryptedApiKey, encryptionOptions.Key);
            using var httpClient = httpClientFactory.CreateClient();

            var result = await costFeed.GetCostsAsync(httpClient, apiKey, periodStart, periodEnd, ct);
            if (result is not null)
            {
                return new ProviderCostResponse(
                    provider.Id, provider.Name, provider.ProviderKey,
                    IsLocal: isLocal, CostApiSupported: true,
                    TotalCost: result.TotalAmount,
                    Currency: result.Currency,
                    PeriodStart: periodStart, PeriodEnd: periodEnd,
                    DailyBreakdown: result.DailyBuckets
                        .Select(b => new CostDailyBucket(b.Start, b.End, b.Amount, result.Currency))
                        .ToList(),
                    Note: isLocal ? "Local provider — no cloud API costs incurred." : null);
            }

            // API returned null — key likely lacks the required billing permissions.
            // The plugin owns the provider-specific remediation message.
            return new ProviderCostResponse(
                provider.Id, provider.Name, provider.ProviderKey,
                IsLocal: isLocal, CostApiSupported: true,
                TotalCost: 0, Currency: DefaultFallbackCurrency,
                PeriodStart: periodStart, PeriodEnd: periodEnd,
                DailyBreakdown: null,
                Note: plugin!.CostFeedPermissionDeniedNote);
        }

        // Provider does not implement a cost API
        return new ProviderCostResponse(
            provider.Id, provider.Name, provider.ProviderKey,
            IsLocal: isLocal, CostApiSupported: false,
            TotalCost: 0, Currency: DefaultFallbackCurrency,
            PeriodStart: periodStart, PeriodEnd: periodEnd,
            DailyBreakdown: null,
            Note: $"Provider key '{provider.ProviderKey}' does not expose a cost API. "
                + "Check the provider's dashboard for billing information.");
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
        var (periodStart, periodEnd) = ResolvePeriod(days, startDate, endDate);

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

        var totalCost = results.Sum(r => r.TotalCost);
        var currency = results.FirstOrDefault(r => r.CostApiSupported && r.TotalCost > 0)?.Currency
            ?? DefaultFallbackCurrency;

        return new ProviderCostTotalResponse(totalCost, currency, periodStart, periodEnd, results);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ResolvePeriod(
        int days, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        var end = endDate ?? DateTimeOffset.UtcNow;
        var start = startDate ?? end.AddDays(-days);
        return (start, end);
    }
}
