using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class GroqApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.groq.com/openai/v1";
    public override ProviderType ProviderType => ProviderType.Groq;
}
