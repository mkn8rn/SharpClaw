namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Events that can trigger a task event-handler callback.
/// </summary>
public enum TaskTriggerKind
{
    /// <summary>
    /// A trigger owned by a module. The module trigger key is stored
    /// alongside this discriminator in <c>TaskStepDefinition.ModuleTriggerKey</c>
    /// and in <c>ITaskEventHandler.ModuleTriggerKey</c>.
    /// </summary>
    ModuleEvent,

    /// <summary>Fired on a periodic timer interval.</summary>
    Timer,

    /// <summary>Fired once when the task starts executing.</summary>
    TaskStarted,

    /// <summary>Fired when the task is being stopped / cancelled.</summary>
    TaskStopping,
}
