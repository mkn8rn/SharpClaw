using SharpClaw.Contracts.Providers;

namespace SharpClaw.Providers.Common;

/// <summary>
/// Catalogue of provider-shape <see cref="CompletionParameterSpec"/>
/// instances. Provider modules pass these to <see cref="SimpleProviderPlugin"/>
/// at registration time so each plugin owns its parameter-validation surface.
/// </summary>
public static class ProviderParameterSpecs
{
    // ─────────────────────────────────────────────────────────
    // OpenAI  (Chat Completions + Responses API)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec OpenAI = new()
    {
        ProviderName = "OpenAI",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high", "xhigh"],
        SupportsToolChoice = true,
    };

    // DeepSeek (OpenAI-compatible Chat Completions)
    public static readonly CompletionParameterSpec DeepSeek = new()
    {
        ProviderName = "DeepSeek",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = false,
        SupportsPresencePenalty = false,
        SupportsStop = true,
        MaxStopSequences = 16,
        SupportsSeed = false,
        SupportsResponseFormat = true,
        OnlyJsonObjectResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["low", "medium", "high", "xhigh", "max"],
        SupportsToolChoice = true,
        SupportsStrictTools = false,
    };

    // ─────────────────────────────────────────────────────────
    // Anthropic
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Anthropic = new()
    {
        ProviderName = "Anthropic",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 1.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = true,
        TopKMin = 1,
        TopKMax = int.MaxValue,
        SupportsFrequencyPenalty = false,
        SupportsPresencePenalty = false,
        SupportsStop = true,
        MaxStopSequences = 8192,
        SupportsSeed = false,
        SupportsResponseFormat = false,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // OpenRouter  (multi-model gateway, OpenAI-compatible)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec OpenRouter = new()
    {
        ProviderName = "OpenRouter",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = true,
        TopKMin = 1,
        TopKMax = int.MaxValue,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // Eden AI (multi-provider gateway, OpenAI-compatible)
    public static readonly CompletionParameterSpec EdenAI = new()
    {
        ProviderName = "Eden AI",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "disable", "minimal", "low", "medium", "high", "xhigh", "max"],
        SupportsToolChoice = true,
    };

    // ─────────────────────────────────────────────────────────
    // Google Vertex AI  (native generateContent)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec GoogleVertexAI = new()
    {
        ProviderName = "Google Vertex AI",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = true,
        TopKMin = 1,
        TopKMax = int.MaxValue,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 5,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high"],
        SupportsToolChoice = true,
    };

    // ─────────────────────────────────────────────────────────
    // Google Vertex AI OpenAI-compat
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec GoogleVertexAIOpenAi = new()
    {
        ProviderName = "Google Vertex AI (OpenAI-compat)",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 5,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        RejectsJsonObjectResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high"],
    };

    // ─────────────────────────────────────────────────────────
    // Google Gemini  (native generateContent)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec GoogleGemini = new()
    {
        ProviderName = "Google Gemini",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = true,
        TopKMin = 1,
        TopKMax = int.MaxValue,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 5,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high"],
        SupportsToolChoice = true,
    };

    // ─────────────────────────────────────────────────────────
    // Google Gemini OpenAI-compat
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec GoogleGeminiOpenAi = new()
    {
        ProviderName = "Google Gemini (OpenAI-compat)",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 5,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        RejectsJsonObjectResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high"],
    };

    // ─────────────────────────────────────────────────────────
    // xAI (Grok)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec XAI = new()
    {
        ProviderName = "xAI (Grok)",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Groq
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Groq = new()
    {
        ProviderName = "Groq",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Cerebras
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Cerebras = new()
    {
        ProviderName = "Cerebras",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 1.5f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = false,
        SupportsPresencePenalty = false,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Mistral
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Mistral = new()
    {
        ProviderName = "Mistral",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 1.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = false,
        SupportsPresencePenalty = false,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // GitHub Copilot
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec GitHubCopilot = new()
    {
        ProviderName = "GitHub Copilot",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        ValidReasoningEffortValues = ["none", "minimal", "low", "medium", "high", "xhigh"],
    };

    // ─────────────────────────────────────────────────────────
    // ZAI
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec ZAI = new()
    {
        ProviderName = "ZAI",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Vercel AI Gateway
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec VercelAIGateway = new()
    {
        ProviderName = "Vercel AI Gateway",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Minimax
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Minimax = new()
    {
        ProviderName = "Minimax",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = false,
        SupportsPresencePenalty = false,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = false,
        SupportsResponseFormat = false,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // LlamaSharp (in-process llama.cpp)
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec LlamaSharp = new()
    {
        ProviderName = "LlamaSharp (Local)",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = true,
        TopKMin = 1,
        TopKMax = 128,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = 0.0f,
        FrequencyPenaltyMax = 1.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = 0.0f,
        PresencePenaltyMax = 1.0f,
        SupportsStop = true,
        MaxStopSequences = 16,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        OnlyJsonObjectResponseFormat = false,
        SupportsReasoningEffort = true,
        ReasoningEffortInformationalOnly = true,
        SupportsToolChoice = true,
        SupportsStrictTools = true,
    };

    // ─────────────────────────────────────────────────────────
    // Ollama
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Ollama = new()
    {
        ProviderName = "Ollama",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        TopPMin = 0.0f,
        TopPMax = 1.0f,
        SupportsTopK = false,
        SupportsFrequencyPenalty = true,
        FrequencyPenaltyMin = -2.0f,
        FrequencyPenaltyMax = 2.0f,
        SupportsPresencePenalty = true,
        PresencePenaltyMin = -2.0f,
        PresencePenaltyMax = 2.0f,
        SupportsStop = true,
        MaxStopSequences = 4,
        SupportsSeed = true,
        SupportsResponseFormat = false,
        SupportsReasoningEffort = false,
    };

    // ─────────────────────────────────────────────────────────
    // Custom (OpenAI-compatible) — permissive passthrough
    // ─────────────────────────────────────────────────────────
    public static readonly CompletionParameterSpec Custom = new()
    {
        ProviderName = "Custom / Unknown",
        SupportsTemperature = true,
        TemperatureMin = 0.0f,
        TemperatureMax = 2.0f,
        SupportsTopP = true,
        SupportsTopK = true,
        SupportsFrequencyPenalty = true,
        SupportsPresencePenalty = true,
        SupportsStop = true,
        MaxStopSequences = 16,
        SupportsSeed = true,
        SupportsResponseFormat = true,
        SupportsReasoningEffort = true,
        SupportsToolChoice = true,
    };

    private static readonly Dictionary<string, CompletionParameterSpec> ByKey = new()
    {
        ["openai"]               = OpenAI,
        ["deepseek"]             = DeepSeek,
        ["anthropic"]            = Anthropic,
        ["openrouter"]           = OpenRouter,
        ["eden-ai"]              = EdenAI,
        ["google-vertex-ai"]     = GoogleVertexAI,
        ["google-vertex-ai-openai"] = GoogleVertexAIOpenAi,
        ["google-gemini"]        = GoogleGemini,
        ["google-gemini-openai"] = GoogleGeminiOpenAi,
        ["xai"]                  = XAI,
        ["groq"]                 = Groq,
        ["cerebras"]             = Cerebras,
        ["mistral"]              = Mistral,
        ["github-copilot"]        = GitHubCopilot,
        ["zai"]                  = ZAI,
        ["vercel-ai-gateway"]      = VercelAIGateway,
        ["minimax"]              = Minimax,
        ["llamasharp"]           = LlamaSharp,
        ["ollama"]               = Ollama,
        ["custom"]               = Custom,
    };

    /// <summary>
    /// Static catalogue lookup by provider key. Returns <see cref="Custom"/>
    /// (the permissive passthrough spec) for unknown keys. Production code
    /// should prefer resolving the spec from the active provider plugin via
    /// <c>ProviderApiClientFactory.GetParameterSpec</c>; this lookup exists
    /// for tests and the validator's back-compat string overload.
    /// </summary>
    public static CompletionParameterSpec For(string providerKey)
        => ByKey.TryGetValue(providerKey, out var spec) ? spec : Custom;
}
