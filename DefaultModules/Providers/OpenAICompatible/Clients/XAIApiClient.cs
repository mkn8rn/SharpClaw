using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class XAIApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.x.ai/v1";
    public override string ProviderKey => "xai";
}
