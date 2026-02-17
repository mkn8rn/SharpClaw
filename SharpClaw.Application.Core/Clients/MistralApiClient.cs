using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class MistralApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.mistral.ai/v1";
    public override ProviderType ProviderType => ProviderType.Mistral;
}
