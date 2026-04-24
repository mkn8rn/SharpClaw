namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Controls how concurrent firings of the same trigger are handled.
/// </summary>
public enum TriggerConcurrency
{
    /// <summary>Skip this firing if the task is already running. Default.</summary>
    SkipIfRunning,
    /// <summary>Queue the firing; run after the current execution completes.</summary>
    Queue,
    /// <summary>Allow multiple simultaneous executions of the same task.</summary>
    Parallel,
    /// <summary>Cancel the running execution and start a new one immediately.</summary>
    CancelAndReplace,
}
