using System.Threading.Channels;
using SharpClaw.Contracts.DTOs.Transcription;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Host-side sink for transcription segment writes and job-status updates.
/// Implemented by <c>AgentJobService</c>; injected into the Transcription
/// module so it never references Core or Infrastructure directly.
/// </summary>
public interface ITranscriptionJobSink
{
    /// <summary>
    /// Persists a transcription segment and pushes it to any active
    /// streaming consumers.
    /// </summary>
    Task<TranscriptionSegmentResponse?> PushSegmentAsync(
        Guid jobId, string text,
        double startTime, double endTime,
        double? confidence = null,
        bool isProvisional = false,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the text of an existing provisional segment without
    /// finalizing it.
    /// </summary>
    Task<bool> UpdateProvisionalTextAsync(
        Guid jobId, Guid segmentId, string text,
        CancellationToken ct = default);

    /// <summary>
    /// Appends an informational entry to the job's log.
    /// Implementations should swallow and log (not throw) storage errors.
    /// </summary>
    Task AddJobLogAsync(
        Guid jobId, string message, string level = "Info");

    /// <summary>
    /// Marks the job as <c>Failed</c>, records the error, and closes the
    /// streaming channel for the job.
    /// </summary>
    Task MarkJobFailedAsync(Guid jobId, Exception ex);
    /// <summary>
    /// Marks all transcription jobs that were left in <c>Executing</c> or
    /// <c>Queued</c> state (from a previous session) as <c>Cancelled</c>.
    /// Called during module <c>InitializeAsync</c> to reconcile stale state.
    /// </summary>
    Task CancelStaleTranscriptionJobsAsync(CancellationToken ct = default);

    /// <summary>
    /// Promotes a provisional segment to final, optionally updating its text.
    /// </summary>
    Task<TranscriptionSegmentResponse?> FinalizeSegmentAsync(
        Guid jobId, Guid segmentId, string text,
        double? confidence = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a live <see cref="ChannelReader{T}"/> for the job's segment stream,
    /// or <see langword="null"/> if the job has no active channel.
    /// </summary>
    ChannelReader<TranscriptionSegmentResponse>? Subscribe(Guid jobId);
}
