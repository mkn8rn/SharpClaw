using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class OpenAiApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.openai.com/v1";
    public override ProviderType ProviderType => ProviderType.OpenAI;
}
