namespace SharpClaw.Contracts.Tasks;

/// <summary>
/// Allows a module to extend the task script parser with additional
/// context-API step kinds and event-trigger handler names.
/// Implementations are registered once at startup via
/// <c>TaskScriptParser.RegisterModule</c>.
/// </summary>
public interface ITaskParserModuleExtension
{
    /// <summary>
    /// Maps context-API method names (as they appear in task scripts) to a
    /// <see cref="TaskStepKind"/> value and the owning module ID.
    /// </summary>
    IReadOnlyDictionary<string, (TaskStepKind Kind, string ModuleId)> StepKindMappings { get; }

    /// <summary>
    /// Maps event-handler method names (as they appear in task scripts) to a
    /// <see cref="TaskTriggerKind"/> value and the owning module ID.
    /// </summary>
    IReadOnlyDictionary<string, (TaskTriggerKind Kind, string ModuleId)> EventTriggerMappings { get; }

    /// <summary>
    /// Method names in <see cref="StepKindMappings"/> whose first argument
    /// should be captured as <c>Expression</c> on the parsed step.
    /// </summary>
    IReadOnlySet<string> SingleArgExpressionMethods { get; }
}
