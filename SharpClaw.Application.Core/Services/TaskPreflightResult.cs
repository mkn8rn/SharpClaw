using SharpClaw.Application.Infrastructure.Tasks;

namespace SharpClaw.Application.Services;

/// <summary>
/// The aggregated outcome of a task preflight check.
/// </summary>
/// <param name="IsBlocked">
/// <c>true</c> when at least one <see cref="TaskDiagnosticSeverity.Error"/>-severity
/// finding did not pass.  A blocked result prevents instance creation.
/// </param>
/// <param name="Findings">Individual results — one per evaluated requirement.</param>
public sealed record TaskPreflightResult(
    bool IsBlocked,
    IReadOnlyList<TaskPreflightFinding> Findings);

/// <summary>
/// The result of evaluating a single requirement during a preflight check.
/// </summary>
/// <param name="RequirementKind">The <see cref="Infrastructure.Tasks.Models.TaskRequirementKind"/> string name.</param>
/// <param name="Severity">Whether this finding is an error or a warning.</param>
/// <param name="Passed">Whether the requirement is satisfied in the current environment.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="ParameterName">Set for parameter-bound requirements; null otherwise.</param>
public sealed record TaskPreflightFinding(
    string RequirementKind,
    TaskDiagnosticSeverity Severity,
    bool Passed,
    string Message,
    string? ParameterName = null);
