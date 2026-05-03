using SharpClaw.Contracts.Providers;

namespace SharpClaw.Providers.Common;

/// <summary>
/// Concrete implementation of <see cref="ICompletionParameterSpec"/> used
/// by provider plugins to declare their parameter constraints.
/// </summary>
public sealed record CompletionParameterSpec : ICompletionParameterSpec
{
    /// <summary>Display-friendly provider name used in error messages.</summary>
    public required string ProviderName { get; init; }

    // ── Temperature ──────────────────────────────────────────────
    public bool SupportsTemperature { get; init; }
    public float TemperatureMin { get; init; }
    public float TemperatureMax { get; init; } = 2.0f;

    // ── Top-P ────────────────────────────────────────────────────
    public bool SupportsTopP { get; init; }
    public float TopPMin { get; init; }
    public float TopPMax { get; init; } = 1.0f;

    // ── Top-K ────────────────────────────────────────────────────
    public bool SupportsTopK { get; init; }
    public int TopKMin { get; init; } = 1;
    public int TopKMax { get; init; } = int.MaxValue;

    // ── Frequency penalty ────────────────────────────────────────
    public bool SupportsFrequencyPenalty { get; init; }
    public float FrequencyPenaltyMin { get; init; } = -2.0f;
    public float FrequencyPenaltyMax { get; init; } = 2.0f;

    // ── Presence penalty ─────────────────────────────────────────
    public bool SupportsPresencePenalty { get; init; }
    public float PresencePenaltyMin { get; init; } = -2.0f;
    public float PresencePenaltyMax { get; init; } = 2.0f;

    // ── Stop sequences ───────────────────────────────────────────
    public bool SupportsStop { get; init; }
    public int MaxStopSequences { get; init; } = 4;

    // ── Seed ─────────────────────────────────────────────────────
    public bool SupportsSeed { get; init; }

    // ── Response format ──────────────────────────────────────────
    public bool SupportsResponseFormat { get; init; }

    /// <inheritdoc/>
    public bool RejectsJsonObjectResponseFormat { get; init; }

    /// <inheritdoc/>
    public bool OnlyJsonObjectResponseFormat { get; init; }

    // ── Reasoning effort ─────────────────────────────────────────
    public bool SupportsReasoningEffort { get; init; }

    /// <inheritdoc/>
    public bool ReasoningEffortInformationalOnly { get; init; }

    public string[] ValidReasoningEffortValues { get; init; } = ["none", "minimal", "low", "medium", "high", "xhigh"];

    // ── Tool choice / parallel tool calls ────────────────────────

    /// <inheritdoc/>
    public bool SupportsToolChoice { get; init; }

    /// <inheritdoc/>
    public bool SupportsStrictTools { get; init; }
}
