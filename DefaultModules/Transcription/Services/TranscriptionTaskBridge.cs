using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.Enums;
using SharpClaw.Contracts.Modules;
using SharpClaw.Modules.Transcription.Contracts;

namespace SharpClaw.Modules.Transcription.Services;

internal sealed class TranscriptionTaskBridge(
    IAgentJobController jobs,
    IAgentJobReader jobReader,
    ILiveTranscriptionOrchestrator orchestrator)
{
    private const string TranscriptionActionPrefix = "transcribe_from_audio";

    public Task<AgentJobResponse> SubmitJobAsync(
        Guid channelId,
        SubmitAgentJobRequest request,
        CancellationToken ct = default) =>
        jobs.SubmitJobAsync(channelId, request, ct);

    public async Task StopTranscriptionJobAsync(Guid jobId, CancellationToken ct = default)
    {
        if (await jobReader.GetJobAsync(jobId, ct) is { Status: AgentJobStatus.Executing })
            orchestrator.Stop(jobId);

        await jobs.StopJobAsync(jobId, TranscriptionActionPrefix, ct);
    }
}
