using SharpClaw.Application.Core.Clients;
using SharpClaw.Application.Core.LocalInference;
using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Application.Core.Providers;

/// <summary>
/// Builds the residual provider plugins that still ship inside Core after
/// Phase 6b. The OpenAI-protocol family was carved out into
/// <c>SharpClaw.Modules.Providers.OpenAICompatible</c>; this assembler now
/// only registers the native (non-OpenAI-shaped) clients plus the local
/// inference plugins. Phases 7-8 split these out into their own modules and
/// delete this file.
/// </summary>
public static class BuiltInProviderPlugins
{
    public static IEnumerable<IProviderPlugin> Build(LocalInferenceProcessManager localInferenceManager)
    {
        var anthropicCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForAnthropic);
        var googleCaps    = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGoogle);
        var genericCaps   = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGeneric);

        return
        [
            new SimpleProviderPlugin(WellKnownProviderKeys.Anthropic,      "Anthropic",        false,
                _ => new AnthropicApiClient(),                              anthropicCaps),
            new SimpleProviderPlugin(WellKnownProviderKeys.GoogleVertexAI, "Google Vertex AI", false,
                _ => new GoogleVertexAIApiClient(),                         googleCaps),
            new SimpleProviderPlugin(WellKnownProviderKeys.GoogleGemini,   "Google Gemini",    false,
                _ => new GoogleGeminiApiClient(),                           googleCaps),
            new SimpleProviderPlugin(WellKnownProviderKeys.LlamaSharp,     "LlamaSharp (local)", false,
                _ => new LocalInferenceApiClient(localInferenceManager),    genericCaps),
            new SimpleProviderPlugin(WellKnownProviderKeys.Ollama,         "Ollama (local)",   false,
                endpoint => new OllamaApiClient(endpoint),                  genericCaps),
        ];
    }
}
