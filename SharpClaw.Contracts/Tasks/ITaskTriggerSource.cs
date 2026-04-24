namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// A trigger source implementation that watches for a particular condition and
/// fires one or more <see cref="ITaskTriggerSourceContext"/> instances when it
/// is satisfied. Implemented by both Core and modules.
/// </summary>
public interface ITaskTriggerSource
{
    /// <summary>
    /// The <see cref="TriggerKind"/> values that this source handles.
    /// A source may handle more than one kind (e.g. <c>TaskCompleted</c> and
    /// <c>TaskFailed</c> are both handled by <c>EventBusTriggerSource</c>).
    /// </summary>
    IReadOnlyList<TriggerKind> SupportedKinds { get; }

    /// <summary>
    /// Optional source name for <see cref="TriggerKind.Custom"/> sources.
    /// Module authors implementing custom trigger sources should return a unique
    /// non-empty name that matches the value used in <c>[OnTrigger("SourceName")]</c>
    /// attributes. Built-in sources may return null.
    /// </summary>
    string? SourceName => null;

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
