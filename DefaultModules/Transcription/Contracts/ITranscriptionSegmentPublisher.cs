using System.Threading.Channels;
using SharpClaw.Modules.Transcription.DTOs;

namespace SharpClaw.Modules.Transcription.Contracts;

internal interface ITranscriptionSegmentPublisher
{
    ChannelReader<TranscriptionSegmentResponse>? Subscribe(Guid jobId);
}
