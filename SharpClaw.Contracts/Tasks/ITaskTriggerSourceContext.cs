namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Runtime context supplied to an <see cref="ITaskTriggerSource"/> when it
/// should fire. The source calls <see cref="FireAsync"/> once the underlying
/// condition is satisfied; the host then creates a task instance and starts it.
/// </summary>
public interface ITaskTriggerSourceContext
{
    /// <summary>The binding row that this context represents.</summary>
    TaskTriggerDefinition Definition { get; }

    /// <summary>The task definition ID this binding belongs to.</summary>
    Guid TaskDefinitionId { get; }

    /// <summary>
    /// Signal that the trigger condition has been met. The host will create
    /// and start a task instance using the supplied parameter overrides.
    /// </summary>
    /// <param name="parameters">
    /// Optional parameter map injected into the new task instance.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task FireAsync(
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default);
}
