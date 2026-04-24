namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Resolves or creates threads within a channel.
/// Implemented host-side by <c>ThreadService</c>; consumed by modules
/// that need to route messages to a channel's default thread without
/// depending on Core or Infrastructure.
/// </summary>
public interface IThreadResolver
{
    /// <summary>
    /// Returns the most recently created thread in <paramref name="channelId"/>,
    /// or creates a "Default" thread if none exist.
    /// </summary>
    Task<Guid> ResolveOrCreateAsync(Guid channelId, CancellationToken ct = default);
}
