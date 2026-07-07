using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharpClaw.Contracts.Providers;

namespace SharpClaw.Application.Core.Clients;

internal static class ProviderClientCompatibilityExtensions
{
    private static readonly ConditionalWeakTable<object, Dictionary<string, MethodInfo>> MethodCache = new();

    public static Task<IReadOnlyList<string>> ListModelIdsAsync(
        this IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        CancellationToken ct = default)
        => InvokeLegacyAsync<Task<IReadOnlyList<string>>>(
            client,
            nameof(IProviderApiClient.ListModelIdsAsync),
            [
                typeof(HttpClient),
                typeof(string),
                typeof(CancellationToken),
            ],
            [httpClient, apiKey, ct]);

    public static Task<ChatCompletionResult> ChatCompletionAsync(
        this IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        string model,
        string? systemPrompt,
        IReadOnlyList<ChatCompletionMessage> messages,
        int? maxCompletionTokens = null,
        Dictionary<string, JsonElement>? providerParameters = null,
        CompletionParameters? completionParameters = null,
        CancellationToken ct = default)
        => InvokeLegacyAsync<Task<ChatCompletionResult>>(
            client,
            nameof(IProviderApiClient.ChatCompletionAsync),
            [
                typeof(HttpClient),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<ChatCompletionMessage>),
                typeof(int?),
                typeof(Dictionary<string, JsonElement>),
                typeof(CompletionParameters),
                typeof(CancellationToken),
            ],
            [
                httpClient,
                apiKey,
                model,
                systemPrompt,
                messages,
                maxCompletionTokens,
                providerParameters,
                completionParameters,
                ct,
            ]);

    public static Task<ChatCompletionResult> ChatCompletionWithToolsAsync(
        this IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        string model,
        string? systemPrompt,
        IReadOnlyList<ToolAwareMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        int? maxCompletionTokens = null,
        Dictionary<string, JsonElement>? providerParameters = null,
        CompletionParameters? completionParameters = null,
        CancellationToken ct = default)
        => InvokeLegacyAsync<Task<ChatCompletionResult>>(
            client,
            nameof(IProviderApiClient.ChatCompletionWithToolsAsync),
            [
                typeof(HttpClient),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<ToolAwareMessage>),
                typeof(IReadOnlyList<ChatToolDefinition>),
                typeof(int?),
                typeof(Dictionary<string, JsonElement>),
                typeof(CompletionParameters),
                typeof(CancellationToken),
            ],
            [
                httpClient,
                apiKey,
                model,
                systemPrompt,
                messages,
                tools,
                maxCompletionTokens,
                providerParameters,
                completionParameters,
                ct,
            ]);

    public static IAsyncEnumerable<ChatStreamChunk> StreamChatCompletionWithToolsAsync(
        this IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        string model,
        string? systemPrompt,
        IReadOnlyList<ToolAwareMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        int? maxCompletionTokens = null,
        Dictionary<string, JsonElement>? providerParameters = null,
        CompletionParameters? completionParameters = null,
        CancellationToken ct = default)
        => InvokeLegacyAsync<IAsyncEnumerable<ChatStreamChunk>>(
            client,
            nameof(IProviderApiClient.StreamChatCompletionWithToolsAsync),
            [
                typeof(HttpClient),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<ToolAwareMessage>),
                typeof(IReadOnlyList<ChatToolDefinition>),
                typeof(int?),
                typeof(Dictionary<string, JsonElement>),
                typeof(CompletionParameters),
                typeof(CancellationToken),
            ],
            [
                httpClient,
                apiKey,
                model,
                systemPrompt,
                messages,
                tools,
                maxCompletionTokens,
                providerParameters,
                completionParameters,
                ct,
            ]);

    public static Task<ProviderCostResult?> GetCostsAsync(
        this IProviderCostFeed feed,
        HttpClient httpClient,
        string apiKey,
        DateTimeOffset startTime,
        DateTimeOffset? endTime,
        CancellationToken ct = default)
        => InvokeLegacyAsync<Task<ProviderCostResult?>>(
            feed,
            nameof(IProviderCostFeed.GetCostsAsync),
            [
                typeof(HttpClient),
                typeof(string),
                typeof(DateTimeOffset),
                typeof(DateTimeOffset?),
                typeof(CancellationToken),
            ],
            [httpClient, apiKey, startTime, endTime, ct]);

    private static T InvokeLegacyAsync<T>(
        object target,
        string methodName,
        Type[] parameterTypes,
        object?[] arguments)
    {
        var key = methodName + "(" + string.Join(",", parameterTypes.Select(type => type.FullName)) + ")";
        var methods = MethodCache.GetOrCreateValue(target);
        if (!methods.TryGetValue(key, out var method))
        {
            method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: parameterTypes,
                modifiers: null)
                ?? throw new NotSupportedException(
                    $"Provider implementation '{target.GetType().FullName}' does not expose legacy host-bound method '{methodName}'.");
            methods[key] = method;
        }

        return (T)method.Invoke(target, arguments)!;
    }
}
