namespace SharpClaw.Contracts.Providers;

/// <summary>
/// Optional live-cost reporting surface contributed by a provider plugin.
/// Plugins for providers that expose a billing/usage API (e.g. OpenAI's
/// Organization Costs endpoint) return a non-null
/// <see cref="IProviderPlugin.CostFeed"/>; everyone else returns
/// <see langword="null"/>. The Core cost service consults the plugin
/// instead of casting the API client, keeping protocol-shape concerns
/// out of the pipeline.
/// </summary>
public interface IProviderCostFeed
{
    /// <summary>
    /// Fetches aggregated cost data from the provider's billing/usage API.
    /// </summary>
    /// <returns>
    /// A <see cref="ProviderCostResult"/> with daily buckets and totals,
    /// or <see langword="null"/> if the API key lacks the required
    /// permissions (e.g. OpenAI admin key requirement) or the request
    /// otherwise fails in a recoverable way.
    /// </returns>
    Task<ProviderCostResult?> GetCostsAsync(
        HttpClient httpClient,
        string apiKey,
        DateTimeOffset startTime,
        DateTimeOffset? endTime,
        CancellationToken ct = default);

    /// <summary>
    /// Human-readable explanation surfaced to the caller when
    /// <see cref="GetCostsAsync"/> returns <see langword="null"/> because the
    /// configured API key lacks the privileges required by the provider's
    /// billing API. Plugins should describe the specific remediation step
    /// (e.g. "OpenAI requires an admin key — replace the key with an admin
    /// key to retrieve cost data."). The default keeps the message generic
    /// so the cost service does not have to know provider-specific semantics.
    /// </summary>
    string PermissionDeniedNote =>
        "Cost API is available for this provider but the current API key "
        + "lacks the required permissions. Update the API key to one with "
        + "billing/usage access to retrieve cost data.";
}

public sealed record ProviderCostResult(
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<ProviderCostDailyBucket> DailyBuckets);

public sealed record ProviderCostDailyBucket(
    DateTimeOffset Start,
    DateTimeOffset End,
    decimal Amount);
