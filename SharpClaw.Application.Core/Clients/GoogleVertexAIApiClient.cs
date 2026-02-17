using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class GoogleVertexAIApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://us-central1-aiplatform.googleapis.com/v1beta1/openai";
    public override ProviderType ProviderType => ProviderType.GoogleVertexAI;
}
