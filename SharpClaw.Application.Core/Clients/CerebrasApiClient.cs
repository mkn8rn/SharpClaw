using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class CerebrasApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.cerebras.ai/v1";
    public override ProviderType ProviderType => ProviderType.Cerebras;
}
