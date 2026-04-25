namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Public projection of a running task instance's execution context.
/// Passed to <see cref="ITaskStepExecutorExtension"/> implementations so modules
/// can read and write task variables, enumerate event handlers, and execute step bodies
/// without taking a dependency on the internal orchestrator or Infrastructure.Tasks.
/// </summary>
public interface ITaskStepExecutionContext
{
    /// <summary>The running task instance ID.</summary>
    Guid InstanceId { get; }

    /// <summary>The channel this task instance is executing against.</summary>
    Guid ChannelId { get; }

    /// <summary>Active cancellation token for the task instance.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Task-script variables. Modules may read and write entries to propagate
    /// results (e.g. a transcription job ID stored via <c>ResultVariable</c>).
    /// </summary>
    IDictionary<string, object?> Variables { get; }

    /// <summary>Registered event handlers in this task instance.</summary>
    IReadOnlyList<ITaskEventHandler> EventHandlers { get; }

    /// <summary>
    /// Resolve an expression string (variable reference or literal) to its
    /// current value within this context.
    /// </summary>
    string ResolveExpression(string expression);

    /// <summary>
    /// Append a log entry to this task instance's log.
    /// </summary>
    Task AppendLogAsync(string message);
}
