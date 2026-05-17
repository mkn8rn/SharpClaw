using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace SharpClaw.Application.Services;

/// <summary>
/// Short-lived singleton cache for chat-path state that is expensive to
/// reconstruct and rarely needs sub-second freshness.
/// </summary>
public sealed class ChatRuntimeStateCache(IConfiguration configuration)
{
    private const int DefaultTtlSeconds = 10;
    private const int MaxTtlSeconds = 300;

    private readonly TimeSpan _ttl = ResolveTtl(configuration);
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_ttl <= TimeSpan.Zero)
            return await factory(ct);

        var now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(key, out var current) && current.ExpiresAt > now)
            return await AwaitEntryAsync<T>(key, current, ct);

        var created = new CacheEntry(
            now.Add(_ttl),
            new Lazy<Task<object?>>(
                async () => await factory(ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        while (true)
        {
            if (_entries.TryAdd(key, created))
                return await AwaitEntryAsync<T>(key, created, ct);

            if (!_entries.TryGetValue(key, out current))
                continue;

            if (current.ExpiresAt > now)
                return await AwaitEntryAsync<T>(key, current, ct);

            if (_entries.TryUpdate(key, created, current))
                return await AwaitEntryAsync<T>(key, created, ct);
        }
    }

    public void Clear() => _entries.Clear();

    private async Task<T?> AwaitEntryAsync<T>(
        string key, CacheEntry entry, CancellationToken ct)
    {
        try
        {
            var value = await entry.Value.Value.WaitAsync(ct);
            if (value is null)
                return default;

            return value is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"Chat runtime cache entry '{key}' contains {value.GetType().FullName}, not {typeof(T).FullName}.");
        }
        catch
        {
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    private static TimeSpan ResolveTtl(IConfiguration configuration)
    {
        var seconds = configuration.GetValue("Chat:RuntimeStateCacheSeconds", DefaultTtlSeconds);
        if (seconds <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(Math.Min(seconds, MaxTtlSeconds));
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAt,
        Lazy<Task<object?>> Value);
}
