using SharpClaw.Contracts.Enums;

namespace SharpClaw.Modules.Transcription.Contracts;

internal interface ILiveTranscriptionOrchestrator
{
    bool SupportsProvider(ProviderType providerType);

    void Start(
        Guid jobId,
        Guid modelId,
        Guid deviceId,
        string? language,
        TranscriptionMode? mode = null,
        int? windowSeconds = null,
        int? stepSeconds = null);

    void Stop(Guid jobId);

    bool IsRunning(Guid jobId);

    Task ResumeTranscriptionJobAsync(Guid jobId, CancellationToken ct = default);

    Task StopAllAsync();
}
