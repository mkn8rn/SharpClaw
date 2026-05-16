using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class MinimaxApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.minimaxi.com/v1";
    public override string ProviderKey => "minimax";
}
