using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

public sealed class AnthropicApiClient : IProviderApiClient
{
    private const string ApiEndpoint = "https://api.anthropic.com/v1";
    private const string ApiVersion = "2023-06-01";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProviderType ProviderType => ProviderType.Anthropic;

    public async Task<IReadOnlyList<string>> ListModelIdsAsync(
        HttpClient httpClient, string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/models");
        AddAuthHeaders(request, apiKey);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ModelsListResponse>(ct);
        return body?.Data?
            .Select(m => m.Id)
            .Where(id => id is not null)
            .Cast<string>()
            .Order()
            .ToList() ?? [];
    }

    public async Task<string> ChatCompletionAsync(
        HttpClient httpClient,
        string apiKey,
        string model,
        string? systemPrompt,
        IReadOnlyList<ChatCompletionMessage> messages,
        CancellationToken ct = default)
    {
        var payload = new MessagesRequest
        {
            Model = model,
            MaxTokens = 4096,
            System = systemPrompt,
            Messages = messages
                .Select(m => new MessagePayload(m.Role, m.Content))
                .ToList()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiEndpoint}/messages");
        AddAuthHeaders(request, apiKey);
        request.Content = JsonContent.Create(payload, options: WriteOptions);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MessagesResponse>(ct);
        return result?.Content?.FirstOrDefault(c => c.Type == "text")?.Text
            ?? throw new InvalidOperationException("No response content from Anthropic.");
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);
    }

    // ── Models listing ────────────────────────────────────────────

    private sealed record ModelsListResponse(
        [property: JsonPropertyName("data")] List<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string? Id);

    // ── Messages (chat completion) ────────────────────────────────

    private sealed class MessagesRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
        [JsonPropertyName("system")] public string? System { get; init; }
        [JsonPropertyName("messages")] public required List<MessagePayload> Messages { get; init; }
    }

    private sealed record MessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MessagesResponse(
        [property: JsonPropertyName("content")] List<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);
}
