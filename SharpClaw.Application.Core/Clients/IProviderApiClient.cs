using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public interface IProviderApiClient
{
    ProviderType ProviderType { get; }

    Task<IReadOnlyList<string>> ListModelIdsAsync(
        HttpClient httpClient, string apiKey, CancellationToken ct = default);

    Task<string> ChatCompletionAsync(
        HttpClient httpClient,
        string apiKey,
        string model,
        string? systemPrompt,
        IReadOnlyList<ChatCompletionMessage> messages,
        CancellationToken ct = default);
}
