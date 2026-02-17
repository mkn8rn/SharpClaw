using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Core.Clients;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Utils.Security;

namespace SharpClaw.Application.Services;

public sealed class ChatService(
    SharpClawDbContext db,
    EncryptionOptions encryptionOptions,
    ProviderApiClientFactory clientFactory,
    IHttpClientFactory httpClientFactory)
{
    private const int MaxHistoryMessages = 50;

    public async Task<ChatResponse> SendMessageAsync(
        Guid agentId, ChatRequest request, CancellationToken ct = default)
    {
        var agent = await db.Agents
            .Include(a => a.Model).ThenInclude(m => m.Provider)
            .FirstOrDefaultAsync(a => a.Id == agentId, ct)
            ?? throw new ArgumentException($"Agent {agentId} not found.");

        var provider = agent.Model.Provider;

        if (string.IsNullOrEmpty(provider.EncryptedApiKey))
            throw new InvalidOperationException("Provider does not have an API key configured.");

        // Build conversation: recent history + new user message
        var history = await db.ChatMessages
            .Where(m => m.AgentId == agentId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxHistoryMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatCompletionMessage(m.Role, m.Content))
            .ToListAsync(ct);

        history.Add(new ChatCompletionMessage("user", request.Message));

        var apiKey = ApiKeyEncryptor.Decrypt(provider.EncryptedApiKey, encryptionOptions.Key);
        var client = clientFactory.GetClient(provider.ProviderType, provider.ApiEndpoint);

        using var httpClient = httpClientFactory.CreateClient();
        var assistantContent = await client.ChatCompletionAsync(
            httpClient, apiKey, agent.Model.Name, agent.SystemPrompt, history, ct);

        // Persist both messages
        var userMessage = new ChatMessageDB
        {
            Role = "user",
            Content = request.Message,
            AgentId = agentId
        };

        var assistantMessage = new ChatMessageDB
        {
            Role = "assistant",
            Content = assistantContent,
            AgentId = agentId
        };

        db.ChatMessages.Add(userMessage);
        db.ChatMessages.Add(assistantMessage);
        await db.SaveChangesAsync(ct);

        return new ChatResponse(
            new ChatMessageResponse(userMessage.Role, userMessage.Content, userMessage.CreatedAt),
            new ChatMessageResponse(assistantMessage.Role, assistantMessage.Content, assistantMessage.CreatedAt));
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid agentId, int limit = 50, CancellationToken ct = default)
    {
        return await db.ChatMessages
            .Where(m => m.AgentId == agentId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse(m.Role, m.Content, m.CreatedAt))
            .ToListAsync(ct);
    }
}
