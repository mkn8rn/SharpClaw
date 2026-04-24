using SharpClaw.Contracts.Enums;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Abstraction over the live-transcription orchestrator so that
/// the host can start/stop/query transcription sessions without a
/// compile-time dependency on the Transcription module.
/// </summary>
public interface ILiveTranscriptionOrchestrator
{
    /// <summary>
    /// Returns <see langword="true"/> when the given provider type has a
    /// registered transcription client implementation.
    /// </summary>
    bool SupportsProvider(ProviderType providerType);

    /// <summary>
    /// Starts live audio capture and transcription for a job.
    /// </summary>
    void Start(
        Guid jobId, Guid modelId, string? deviceIdentifier, string? language,
        TranscriptionMode? mode = null, int? windowSeconds = null, int? stepSeconds = null);

    /// <summary>
    /// Stops the capture loop for a job.
    /// </summary>
    void Stop(Guid jobId);

    /// <summary>
    /// Returns <see langword="true"/> when the given job is actively
    /// capturing audio.
    /// </summary>
    bool IsRunning(Guid jobId);

    /// <summary>
    /// Stops all active transcription sessions. Called during graceful
    /// shutdown to drain sessions before the application exits.
    /// </summary>
    Task StopAllAsync()
    {
        return Task.CompletedTask;
    }
}
