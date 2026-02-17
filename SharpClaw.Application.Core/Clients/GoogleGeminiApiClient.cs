using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class GoogleGeminiApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://generativelanguage.googleapis.com/v1beta/openai";
    public override ProviderType ProviderType => ProviderType.GoogleGemini;
}
