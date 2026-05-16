using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class EdenAIApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.edenai.run/v3";
    public override string ProviderKey => "eden-ai";
}
