namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Describes how the host should treat a module job after the module's
/// start method returns.
/// </summary>
public enum ModuleJobCompletionBehavior
{
    /// <summary>
    /// The job completes successfully when the module tool returns without
    /// throwing.
    /// </summary>
    CompleteWhenExecutionReturns = 0,

    /// <summary>
    /// The module tool only starts background work. The job remains executing
    /// until a lifecycle operation or module callback transitions it.
    /// </summary>
    RemainExecuting = 1
}
