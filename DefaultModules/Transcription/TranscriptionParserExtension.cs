using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.Transcription;

/// <summary>
/// Registers the Transcription module's task-script API methods and event-handler
/// names with <see cref="TaskScriptParser"/>.
/// </summary>
internal sealed class TranscriptionParserExtension : ITaskParserModuleExtension
{
    public static readonly TranscriptionParserExtension Instance = new();

    public IReadOnlyDictionary<string, (string StepKey, string ModuleId)> StepKeyMappings { get; } =
        new Dictionary<string, (string, string)>
        {
            ["StartTranscription"]   = ("StartTranscription",   "sharpclaw_transcription"),
            ["StopTranscription"]    = ("StopTranscription",    "sharpclaw_transcription"),
            ["GetDefaultInputAudio"] = ("GetDefaultInputAudio", "sharpclaw_transcription"),
        };

    public IReadOnlyDictionary<string, (string TriggerKey, string ModuleId)> EventTriggerMappings { get; } =
        new Dictionary<string, (string, string)>
        {
            ["OnTranscriptionSegment"] = ("TranscriptionSegment", "sharpclaw_transcription"),
        };

    public IReadOnlySet<string> SingleArgExpressionMethods { get; } =
        new HashSet<string> { "StartTranscription", "StopTranscription" };
}
