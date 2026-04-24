using System.Threading.Channels;
using SharpClaw.Contracts.DTOs.Transcription;

namespace SharpClaw.Contracts.Modules;

/// <summary>
/// Allows streaming consumers (WebSocket / SSE endpoints) to subscribe to
/// live transcription segments for a given job without depending on the
/// host-internal <c>AgentJobService</c>.
/// Implemented by the host; injected into the Transcription module.
/// </summary>
public interface ITranscriptionSegmentPublisher
{
    /// <summary>
    /// Returns a <see cref="ChannelReader{T}"/> for live segment updates for
    /// the given job, or <see langword="null"/> when no active session exists.
    /// </summary>
    ChannelReader<TranscriptionSegmentResponse>? Subscribe(Guid jobId);
}
