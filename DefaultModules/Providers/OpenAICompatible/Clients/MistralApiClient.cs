using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class MistralApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.mistral.ai/v1";
    public override string ProviderKey => "mistral";
}
