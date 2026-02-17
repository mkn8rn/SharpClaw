using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class OpenRouterApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://openrouter.ai/api/v1";
    public override ProviderType ProviderType => ProviderType.OpenRouter;
}
