using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class XAIApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.x.ai/v1";
    public override ProviderType ProviderType => ProviderType.XAI;
}
