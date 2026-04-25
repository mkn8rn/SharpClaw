using SharpClaw.Contracts.Entities;
using SharpClaw.Modules.Transcription.Contracts;

namespace SharpClaw.Modules.Transcription.Models;

/// <summary>
/// Module-owned record of transcription-specific job parameters.
/// Keyed to the host <c>AgentJobDB</c> via <see cref="AgentJobId"/> (bare FK, no nav).
/// </summary>
public class TranscriptionJobDB : BaseEntity
{
    public Guid AgentJobId { get; set; }
    public Guid ModelId { get; set; }
    public Guid DeviceId { get; set; }
    public string? Language { get; set; }
    public TranscriptionMode? Mode { get; set; }
    public int? WindowSeconds { get; set; }
    public int? StepSeconds { get; set; }
}
