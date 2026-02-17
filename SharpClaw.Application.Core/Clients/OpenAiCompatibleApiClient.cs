using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SharpClaw.Contracts.Enums;

namespace SharpClaw.Application.Core.Clients;

/// <summary>
/// Base class for providers that expose OpenAI-compatible
/// <c>GET /models</c> and <c>POST /chat/completions</c> endpoints
/// with Bearer token authentication.
/// </summary>
public abstract class OpenAiCompatibleApiClient : IProviderApiClient
{
    protected abstract string ApiEndpoint { get; }
    public abstract ProviderType ProviderType { get; }

    public async Task<IReadOnlyList<string>> ListModelIdsAsync(
        HttpClient httpClient, string apiKey, CancellationToken ct = default)
    {
        var resolvedKey = await ResolveApiKeyAsync(httpClient, apiKey, ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiEndpoint}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolvedKey);
        ConfigureRequest(request);

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
        var resolvedKey = await ResolveApiKeyAsync(httpClient, apiKey, ct);

        var payloadMessages = new List<CompletionMessagePayload>();

        if (systemPrompt is not null)
            payloadMessages.Add(new CompletionMessagePayload("system", systemPrompt));

        foreach (var msg in messages)
            payloadMessages.Add(new CompletionMessagePayload(msg.Role, msg.Content));

        var payload = new CompletionRequest(model, payloadMessages);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiEndpoint}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolvedKey);
        ConfigureRequest(request);
        request.Content = JsonContent.Create(payload);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CompletionResponse>(ct);
        return result?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("No response content from provider.");
    }

    /// <summary>
    /// Resolves the API key to use for requests. Override in subclasses that
    /// require a token exchange (e.g. GitHub Copilot OAuth → Copilot token).
    /// </summary>
    protected virtual ValueTask<string> ResolveApiKeyAsync(
        HttpClient httpClient, string apiKey, CancellationToken ct)
        => ValueTask.FromResult(apiKey);

    /// <summary>
    /// Allows subclasses to add provider-specific headers to outgoing API requests.
    /// </summary>
    protected virtual void ConfigureRequest(HttpRequestMessage request) { }

    // ── Models listing ────────────────────────────────────────────

    private sealed record ModelsListResponse(
        [property: JsonPropertyName("data")] List<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string? Id);

    // ── Chat completion ───────────────────────────────────────────

    private sealed record CompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<CompletionMessagePayload> Messages);

    private sealed record CompletionMessagePayload(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] List<CompletionChoice>? Choices);

    private sealed record CompletionChoice(
        [property: JsonPropertyName("message")] CompletionMessagePayload? Message);
}
