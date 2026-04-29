using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class OpenRouterApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://openrouter.ai/api/v1";
    public override string ProviderKey => WellKnownProviderKeys.OpenRouter;
}
