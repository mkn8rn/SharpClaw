using SharpClaw.Contracts.Entities;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Runtime.INF.Persistence;

/// <summary>
/// Runtime-owned compact audit row for an execution lifecycle transition.
/// Arbitrary messages and payloads are never stored in this table.
/// </summary>
public sealed class ExecutionAuditEventDB : BaseEntity
{
    public ExecutionOwnerKind OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public required string EventKind { get; set; }
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string? ActorKind { get; set; }
    public Guid? ActorId { get; set; }
    public string? ReasonCode { get; set; }
}

/// <summary>Names for Runtime-owned EF shadow metadata.</summary>
public static class ExecutionMetadataColumns
{
    public const string ResultArtifactId = nameof(ResultArtifactId);
    public const string ResultMediaType = nameof(ResultMediaType);
    public const string ResultLength = nameof(ResultLength);
    public const string ResultSha256 = nameof(ResultSha256);
    public const string ResultPreview = nameof(ResultPreview);
    public const string ErrorCode = nameof(ErrorCode);
    public const string ErrorMessage = nameof(ErrorMessage);
    public const string DiagnosticCompleteness = nameof(DiagnosticCompleteness);
    public const string FinalLogSequence = nameof(FinalLogSequence);
    public const string LogRecordCount = nameof(LogRecordCount);
    public const string FinalOutputSequence = nameof(FinalOutputSequence);
    public const string OutputRecordCount = nameof(OutputRecordCount);
}
