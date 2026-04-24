namespace SharpClaw.Application.Infrastructure.Tasks.Models;

/// <summary>
/// A single environment requirement declared on a task class via a
/// requirement attribute such as <c>[RequiresProvider]</c>, <c>[RequiresModule]</c>,
/// <c>[ModelId]</c>, etc.
/// Produced by <see cref="Parsing.TaskScriptParser"/> and stored as JSON in
/// <c>TaskDefinitionDB.RequirementsJson</c> so the preflight checker and UI
/// can act on them without re-parsing the source.
/// </summary>
public sealed record TaskRequirementDefinition
{
    /// <summary>What kind of environment fact this requirement asserts.</summary>
    public required TaskRequirementKind Kind { get; init; }

    /// <summary>
    /// Whether a failure blocks execution (<see cref="TaskDiagnosticSeverity.Error"/>)
    /// or merely warns (<see cref="TaskDiagnosticSeverity.Warning"/>).
    /// </summary>
    public required TaskDiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// Kind-specific string payload:
    /// <list type="bullet">
    ///   <item><see cref="TaskRequirementKind.RequiresProvider"/> — the ProviderType name (e.g. <c>"Anthropic"</c>).</item>
    ///   <item><see cref="TaskRequirementKind.RequiresModule"/> / <see cref="TaskRequirementKind.RecommendsModule"/> — the module ID.</item>
    ///   <item><see cref="TaskRequirementKind.RequiresPlatform"/> — the <see cref="TaskPlatform"/> flag name (e.g. <c>"Windows"</c>).</item>
    ///   <item><see cref="TaskRequirementKind.RequiresModel"/> — the model name or custom ID.</item>
    ///   <item><see cref="TaskRequirementKind.RequiresPermission"/> — the FlagKey string.</item>
    /// </list>
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Capability flag name for <see cref="TaskRequirementKind.RequiresModelCapability"/>
    /// and <see cref="TaskRequirementKind.RequiresCapabilityParameter"/> (e.g. <c>"Vision"</c>).
    /// </summary>
    public string? CapabilityValue { get; init; }

    /// <summary>
    /// For parameter-level annotations (<see cref="TaskRequirementKind.ModelIdParameter"/>,
    /// <see cref="TaskRequirementKind.RequiresCapabilityParameter"/>): the name of the
    /// task parameter this requirement is bound to.
    /// </summary>
    public string? ParameterName { get; init; }

    /// <summary>Source line for diagnostics (1-based).</summary>
    public int Line { get; init; }
}
