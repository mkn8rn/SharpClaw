using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class VercelAIGatewayApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://gateway.ai.vercel.app/v1";
    public override string ProviderKey => "vercel-ai-gateway";
}
