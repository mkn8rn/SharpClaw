namespace SharpClaw.Contracts.Providers;

/// <summary>
/// Declares what a provider supports and the valid ranges for each
/// completion parameter.  This is the single source of truth that drives
/// both validation (<c>CompletionParameterValidator</c>) and the
/// generated provider parameter reference documentation.
/// </summary>
public interface ICompletionParameterSpec
{
    /// <summary>Display-friendly provider name used in error messages.</summary>
    string ProviderName { get; }

    // ── Temperature ──────────────────────────────────────────────
    bool SupportsTemperature { get; }
    float TemperatureMin { get; }
    float TemperatureMax { get; }

    // ── Top-P ────────────────────────────────────────────────────
    bool SupportsTopP { get; }
    float TopPMin { get; }
    float TopPMax { get; }

    // ── Top-K ────────────────────────────────────────────────────
    bool SupportsTopK { get; }
    int TopKMin { get; }
    int TopKMax { get; }

    // ── Frequency penalty ────────────────────────────────────────
    bool SupportsFrequencyPenalty { get; }
    float FrequencyPenaltyMin { get; }
    float FrequencyPenaltyMax { get; }

    // ── Presence penalty ─────────────────────────────────────────
    bool SupportsPresencePenalty { get; }
    float PresencePenaltyMin { get; }
    float PresencePenaltyMax { get; }

    // ── Stop sequences ───────────────────────────────────────────
    bool SupportsStop { get; }
    int MaxStopSequences { get; }

    // ── Seed ─────────────────────────────────────────────────────
    bool SupportsSeed { get; }

    // ── Response format ──────────────────────────────────────────
    bool SupportsResponseFormat { get; }

    /// <summary>
    /// When <see langword="true"/>, the provider supports <c>response_format</c>
    /// but rejects the simplified <c>{"type": "json_object"}</c> form.
    /// Google's OpenAI compatibility endpoint only accepts the full
    /// <c>{"type": "json_schema", …}</c> variant.
    /// </summary>
    bool RejectsJsonObjectResponseFormat { get; }

    /// <summary>
    /// When <see langword="true"/>, the provider supports <c>response_format</c>
    /// <em>only</em> in the simplified <c>{"type": "json_object"}</c> form.
    /// The structured <c>json_schema</c> variant is not implemented and is
    /// rejected upstream. No provider currently sets this flag.
    /// </summary>
    bool OnlyJsonObjectResponseFormat { get; }

    // ── Reasoning effort ─────────────────────────────────────────
    bool SupportsReasoningEffort { get; }

    /// <summary>
    /// When <see langword="true"/>, <c>ReasoningEffort</c>
    /// is accepted by the validator but is <em>not</em> mapped into the
    /// provider wire payload. The value is surfaced only as a notice inside
    /// the chat header so the model can see the user's intent. Used by
    /// providers whose local runtime has no mechanical reasoning-effort control.
    /// </summary>
    bool ReasoningEffortInformationalOnly { get; }

    string[] ValidReasoningEffortValues { get; }

    // ── Tool choice / parallel tool calls ────────────────────────

    /// <summary>
    /// When <see langword="true"/>, the provider honours the full
    /// <c>tool_choice</c> surface (<c>auto</c>, <c>none</c>,
    /// <c>required</c>, named function) and <c>parallel_tool_calls</c>.
    /// OpenAI-compatible providers forward the fields on the wire;
    /// LlamaSharp enforces them by compiling a tailored GBNF grammar.
    /// </summary>
    bool SupportsToolChoice { get; }

    /// <summary>
    /// When <see langword="true"/>, the provider can enforce each
    /// tool's argument JSON Schema at the sampler/server level
    /// (OpenAI <c>strict: true</c>; LlamaSharp per-tool GBNF).
    /// When <see langword="false"/> the <c>StrictTools</c>
    /// field is accepted but is not enforced mechanically.
    /// </summary>
    bool SupportsStrictTools { get; }

    /// <summary>
    /// Permissive fallback for unknown/custom providers — everything is
    /// "supported" with wide ranges so that validation never blocks.
    /// </summary>
    static ICompletionParameterSpec Passthrough { get; } = new PassthroughSpec();

    private sealed class PassthroughSpec : ICompletionParameterSpec
    {
        public string ProviderName => "Custom / Unknown";
        public bool SupportsTemperature => true;
        public float TemperatureMin => 0.0f;
        public float TemperatureMax => 2.0f;
        public bool SupportsTopP => true;
        public float TopPMin => 0.0f;
        public float TopPMax => 1.0f;
        public bool SupportsTopK => true;
        public int TopKMin => 1;
        public int TopKMax => int.MaxValue;
        public bool SupportsFrequencyPenalty => true;
        public float FrequencyPenaltyMin => -2.0f;
        public float FrequencyPenaltyMax => 2.0f;
        public bool SupportsPresencePenalty => true;
        public float PresencePenaltyMin => -2.0f;
        public float PresencePenaltyMax => 2.0f;
        public bool SupportsStop => true;
        public int MaxStopSequences => 16;
        public bool SupportsSeed => true;
        public bool SupportsResponseFormat => true;
        public bool RejectsJsonObjectResponseFormat => false;
        public bool OnlyJsonObjectResponseFormat => false;
        public bool SupportsReasoningEffort => true;
        public bool ReasoningEffortInformationalOnly => false;
        public string[] ValidReasoningEffortValues => ["none", "minimal", "low", "medium", "high", "xhigh"];
        public bool SupportsToolChoice => true;
        public bool SupportsStrictTools => false;
    }
}
