using SharpClaw.Contracts.Entities;

namespace SharpClaw.Modules.Transcription.Models;

/// <summary>
/// A single segment of transcribed text produced by a live transcription job.
/// AgentJobId is stored as a bare <see cref="Guid"/> FK; no nav property.
/// </summary>
public class TranscriptionSegmentDB : BaseEntity
{
    public Guid AgentJobId { get; set; }
    public required string Text { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double? Confidence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsProvisional { get; set; }
}
