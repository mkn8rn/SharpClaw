using SharpClaw.Contracts.Providers;
using SharpClaw.Core.Chat;

namespace SharpClaw.Application.Services;

internal sealed class ChatProviderRoundExecutor(
    IProviderApiClient client,
    HttpClient httpClient,
    string apiKey) : IChatProviderRoundExecutor
{
    public Task<ChatCompletionResult> CompleteAsync(
        ChatProviderCompletionRequest request,
        CancellationToken ct) =>
        client.ChatCompletionAsync(
            httpClient,
            apiKey,
            request.ModelName,
            request.SystemPrompt,
            request.History,
            request.MaxCompletionTokens,
            request.ProviderParameters,
            request.CompletionParameters,
            ct);

    public Task<ChatCompletionResult> CompleteWithToolsAsync(
        ChatProviderToolCompletionRequest request,
        CancellationToken ct) =>
        client.ChatCompletionWithToolsAsync(
            httpClient,
            apiKey,
            request.ModelName,
            request.SystemPrompt,
            request.Messages,
            request.Tools,
            request.MaxCompletionTokens,
            request.ProviderParameters,
            request.CompletionParameters,
            ct);

    public IAsyncEnumerable<ChatStreamChunk> StreamWithToolsAsync(
        ChatProviderToolCompletionRequest request,
        CancellationToken ct) =>
        client.StreamChatCompletionWithToolsAsync(
            httpClient,
            apiKey,
            request.ModelName,
            request.SystemPrompt,
            request.Messages,
            request.Tools,
            request.MaxCompletionTokens,
            request.ProviderParameters,
            request.CompletionParameters,
            ct);
}
