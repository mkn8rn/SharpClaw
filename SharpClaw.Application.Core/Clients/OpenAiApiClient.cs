using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class OpenAiApiClient : OpenAiCompatibleApiClient
{
    protected override string ApiEndpoint => "https://api.openai.com/v1";
    public override ProviderType ProviderType => ProviderType.OpenAI;

    /// <summary>
    /// Prefer the Responses API for all models except legacy GPT-3.5/GPT-4
    /// families that predate it.
    /// </summary>
    protected override bool UseResponsesApi(string model)
        => !RequiresLegacyChatCompletions(model);
}
