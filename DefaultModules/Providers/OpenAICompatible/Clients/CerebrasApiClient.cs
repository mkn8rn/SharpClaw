using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class CerebrasApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.cerebras.ai/v1";
    public override string ProviderKey => WellKnownProviderKeys.Cerebras;
}
