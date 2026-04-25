using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Modules;

namespace SharpClaw.Modules.Transcription.Services;

public sealed class TranscriptionJobSink(
    IAgentJobController jobs,
    ILogger<TranscriptionJobSink> logger)
{
    private const string TranscriptionActionPrefix = "transcribe_from_audio";

    public async Task AddJobLogAsync(Guid jobId, string message, string level = "Info")
    {
        try
        {
            await jobs.AddJobLogAsync(jobId, message, level);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Unable to add transcription job log for {JobId}.", jobId);
        }
    }

    public async Task MarkJobFailedAsync(Guid jobId, Exception ex)
    {
        try
        {
            await jobs.MarkJobFailedAsync(jobId, ex);
        }
        catch (InvalidOperationException dbEx)
        {
            logger.LogWarning(dbEx, "Unable to mark transcription job {JobId} failed.", jobId);
        }
    }

    public async Task CancelStaleTranscriptionJobsAsync(CancellationToken ct = default)
    {
        await jobs.CancelStaleJobsByActionPrefixAsync(TranscriptionActionPrefix, ct);
    }
}
