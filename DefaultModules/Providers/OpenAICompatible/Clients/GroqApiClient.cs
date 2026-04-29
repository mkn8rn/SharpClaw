using SharpClaw.Contracts.Providers;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible.Clients;

public sealed class GroqApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.groq.com/openai/v1";
    public override string ProviderKey => WellKnownProviderKeys.Groq;
}
