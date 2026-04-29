using SharpClaw.Application.Core.Clients;
using SharpClaw.Application.Core.LocalInference;
using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Application.Core.Providers;

/// <summary>
/// Builds the residual provider plugins that still ship inside Core after
/// Phase 7. The OpenAI-protocol family is owned by
/// <c>SharpClaw.Modules.Providers.OpenAICompatible</c>; Anthropic by
/// <c>SharpClaw.Modules.Providers.Anthropic</c>; Google native by
/// <c>SharpClaw.Modules.Providers.Google</c>. This assembler now only
/// registers the local-inference plugins. Phase 8 carves these out and
/// deletes this file.
/// </summary>
public static class BuiltInProviderPlugins
{
    public static IEnumerable<IProviderPlugin> Build(LocalInferenceProcessManager localInferenceManager)
    {
        var genericCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGeneric);

        return
        [
            new SimpleProviderPlugin(WellKnownProviderKeys.LlamaSharp, "LlamaSharp (local)", false,
                _ => new LocalInferenceApiClient(localInferenceManager), genericCaps),
            new SimpleProviderPlugin(WellKnownProviderKeys.Ollama, "Ollama (local)", false,
                endpoint => new OllamaApiClient(endpoint), genericCaps),
        ];
    }
}
