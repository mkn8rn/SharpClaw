namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// A trigger source implementation that watches for a particular condition and
/// fires one or more <see cref="ITaskTriggerSourceContext"/> instances when it
/// is satisfied. Implemented by both Core and modules.
/// </summary>
public interface ITaskTriggerSource
{
    /// <summary>
    /// String key for sources that handle a single trigger kind.
    /// The host routes binding rows whose <c>Kind</c> column matches this value.
    /// Sources that handle multiple kinds override <see cref="TriggerKeys"/> instead.
    /// </summary>
    string? TriggerKey => null;

    /// <summary>
    /// All string keys handled by this source. Defaults to a single-element list
    /// derived from <see cref="TriggerKey"/> when non-null, or empty if neither
    /// is overridden (invalid — every source must expose at least one key).
    /// Multi-kind sources override this property directly.
    /// </summary>
    IReadOnlyList<string> TriggerKeys => TriggerKey is not null ? [TriggerKey] : [];

    /// <summary>
    /// Start watching. Called by the host when bindings are loaded or reloaded.
    /// Implementations should be idempotent.
    /// </summary>
    /// <param name="contexts">
    /// All active binding contexts for the kinds this source handles.
    /// </param>
    /// <param name="ct">Cancellation token — cancelled when the host shuts down.</param>
    Task StartAsync(IReadOnlyList<ITaskTriggerSourceContext> contexts, CancellationToken ct);

    /// <summary>
    /// Stop watching and release all resources held by this source.
    /// </summary>
    Task StopAsync();
}
