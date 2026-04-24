using SharpClaw.Contracts.Tasks;

namespace SharpClaw.Modules.Transcription;

/// <summary>
/// Registers the Transcription module's task-script API methods and event-handler
/// names with <see cref="TaskScriptParser"/>.
/// </summary>
internal sealed class TranscriptionParserExtension : ITaskParserModuleExtension
{
    public static readonly TranscriptionParserExtension Instance = new();

    public IReadOnlyDictionary<string, (TaskStepKind Kind, string ModuleId)> StepKindMappings { get; } =
        new Dictionary<string, (TaskStepKind, string)>
        {
            ["StartTranscription"]   = (TaskStepKind.StartTranscription,   "sharpclaw_transcription"),
            ["StopTranscription"]    = (TaskStepKind.StopTranscription,    "sharpclaw_transcription"),
            ["GetDefaultInputAudio"] = (TaskStepKind.GetDefaultInputAudio, "sharpclaw_transcription"),
        };

    public IReadOnlyDictionary<string, (TaskTriggerKind Kind, string ModuleId)> EventTriggerMappings { get; } =
        new Dictionary<string, (TaskTriggerKind, string)>
        {
            ["OnTranscriptionSegment"] = (TaskTriggerKind.TranscriptionSegment, "sharpclaw_transcription"),
        };

    public IReadOnlySet<string> SingleArgExpressionMethods { get; } =
        new HashSet<string> { "StartTranscription", "StopTranscription" };
}
