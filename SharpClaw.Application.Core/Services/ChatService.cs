using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SharpClaw.Application.Core.Clients;
using SharpClaw.Application.Infrastructure.Models.Messages;
using SharpClaw.Contracts.DTOs.AgentActions;
using SharpClaw.Contracts.DTOs.Chat;
using SharpClaw.Contracts.Enums;
using SharpClaw.Infrastructure.Models;
using SharpClaw.Infrastructure.Persistence;
using SharpClaw.Utils.Security;

namespace SharpClaw.Application.Services;

public sealed partial class ChatService(
    SharpClawDbContext db,
    EncryptionOptions encryptionOptions,
    ProviderApiClientFactory clientFactory,
    IHttpClientFactory httpClientFactory,
    AgentJobService jobService)
{
    private const int MaxHistoryMessages = 50;

    /// <summary>
    /// Maximum number of tool-call round-trips before forcing a final
    /// response.  Prevents infinite loops when the model keeps emitting
    /// tool calls.
    /// </summary>
    private const int MaxToolCallRounds = 10;

    public async Task<ChatResponse> SendMessageAsync(
        Guid conversationId, ChatRequest request, CancellationToken ct = default)
    {
        var conversation = await db.Conversations
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new ArgumentException($"Conversation {conversationId} not found.");

        var model = conversation.Model;
        var provider = model.Provider;
        var agent = conversation.Agent;

        if (string.IsNullOrEmpty(provider.EncryptedApiKey))
            throw new InvalidOperationException("Provider does not have an API key configured.");

        // Build conversation: recent history + new user message
        var history = await db.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxHistoryMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatCompletionMessage(m.Role, m.Content))
            .ToListAsync(ct);

        history.Add(new ChatCompletionMessage("user", request.Message));

        var apiKey = ApiKeyEncryptor.Decrypt(provider.EncryptedApiKey, encryptionOptions.Key);
        var client = clientFactory.GetClient(provider.ProviderType, provider.ApiEndpoint);
        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt);

        using var httpClient = httpClientFactory.CreateClient();

        // ── Tool-call loop ────────────────────────────────────────
        // The agent is both the actor and the caller — no user identity
        // is injected here.  The conversation's permission set (defined
        // by the user) determines the effective clearance via the
        // existing AgentJobService → AgentActionService pipeline.
        //
        // When the clearance is Independent the job executes inline.
        // Any other clearance means the job goes to AwaitingApproval
        // and we STOP the loop so the user can approve/deny before
        // the chat continues.
        var jobResults = new List<AgentJobResponse>();
        string assistantContent;
        var rounds = 0;

        while (true)
        {
            assistantContent = await client.ChatCompletionAsync(
                httpClient, apiKey, model.Name, systemPrompt, history, ct);

            var toolCalls = ParseToolCalls(assistantContent);
            if (toolCalls.Count == 0 || ++rounds > MaxToolCallRounds)
                break;

            // Record the assistant turn that contains tool calls
            history.Add(new ChatCompletionMessage("assistant", assistantContent));

            // Execute each tool call through the existing job pipeline.
            // CallerAgentId = agent.Id — the agent itself is the caller.
            // No CallerUserId — user approval happens out-of-band via
            // the /approve endpoint if the clearance requires it.
            var toolResultBuilder = new StringBuilder();
            var anyAwaitingApproval = false;

            foreach (var call in toolCalls)
            {
                var jobRequest = new SubmitAgentJobRequest(
                    ActionType: call.ActionType,
                    ResourceId: call.ResourceId,
                    CallerAgentId: agent.Id,
                    DangerousShellType: call.DangerousShellType,
                    SafeShellType: call.SafeShellType,
                    ConversationId: conversationId);

                var jobResponse = await jobService.SubmitAsync(
                    agent.Id, jobRequest, ct);

                jobResults.Add(jobResponse);

                toolResultBuilder.AppendLine(
                    $"[TOOL_RESULT:{call.CallId}] status={jobResponse.Status}" +
                    (jobResponse.ResultData is not null ? $" result={jobResponse.ResultData}" : "") +
                    (jobResponse.ErrorLog is not null ? $" error={jobResponse.ErrorLog}" : ""));

                if (jobResponse.Status == AgentJobStatus.AwaitingApproval)
                    anyAwaitingApproval = true;
            }

            // Feed results back so the model can produce a response.
            history.Add(new ChatCompletionMessage("user", toolResultBuilder.ToString()));

            // If any job is awaiting approval, let the model produce
            // ONE final response explaining what needs approval, then
            // stop — the user must approve/deny before we continue.
            if (anyAwaitingApproval)
            {
                assistantContent = await client.ChatCompletionAsync(
                    httpClient, apiKey, model.Name, systemPrompt, history, ct);
                break;
            }
        }

        // Strip any remaining tool-call blocks from the final response
        assistantContent = StripToolCallBlocks(assistantContent);

        // Persist both messages
        var userMessage = new ChatMessageDB
        {
            Role = "user",
            Content = request.Message,
            ConversationId = conversationId
        };

        var assistantMessage = new ChatMessageDB
        {
            Role = "assistant",
            Content = assistantContent,
            ConversationId = conversationId
        };

        db.ChatMessages.Add(userMessage);
        db.ChatMessages.Add(assistantMessage);
        await db.SaveChangesAsync(ct);

        return new ChatResponse(
            new ChatMessageResponse(userMessage.Role, userMessage.Content, userMessage.CreatedAt),
            new ChatMessageResponse(assistantMessage.Role, assistantMessage.Content, assistantMessage.CreatedAt),
            jobResults.Count > 0 ? jobResults : null);
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid conversationId, int limit = 50, CancellationToken ct = default)
    {
        return await db.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse(m.Role, m.Content, m.CreatedAt))
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool-call parsing
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches tool-call blocks emitted by the model:
    /// <c>[TOOL_CALL:id] {"actionType":"...","resourceId":"...",...}</c>
    /// </summary>
    [GeneratedRegex(
        @"\[TOOL_CALL:(?<id>[^\]]+)\]\s*(?<json>\{[^}]+\})",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ToolCallPattern();

    private static IReadOnlyList<ParsedToolCall> ParseToolCalls(string content)
    {
        var matches = ToolCallPattern().Matches(content);
        if (matches.Count == 0)
            return [];

        var calls = new List<ParsedToolCall>(matches.Count);
        foreach (Match match in matches)
        {
            var callId = match.Groups["id"].Value;
            var json = match.Groups["json"].Value;

            try
            {
                var payload = JsonSerializer.Deserialize<ToolCallPayload>(json, JsonOptions);
                if (payload?.ActionType is not null
                    && Enum.TryParse<AgentActionType>(payload.ActionType, ignoreCase: true, out var actionType))
                {
                    calls.Add(new ParsedToolCall(
                        callId,
                        actionType,
                        payload.ResourceId,
                        payload.DangerousShellType is not null
                            && Enum.TryParse<DangerousShellType>(payload.DangerousShellType, ignoreCase: true, out var dst)
                            ? dst : null,
                        payload.SafeShellType is not null
                            && Enum.TryParse<SafeShellType>(payload.SafeShellType, ignoreCase: true, out var sst)
                            ? sst : null));
                }
            }
            catch (JsonException)
            {
                // Malformed JSON — skip this call
            }
        }

        return calls;
    }

    private static string StripToolCallBlocks(string content)
        => ToolCallPattern().Replace(content, "").Trim();

    // ═══════════════════════════════════════════════════════════════
    // System prompt
    // ═══════════════════════════════════════════════════════════════

    private static string BuildSystemPrompt(string? agentPrompt)
    {
        if (string.IsNullOrEmpty(agentPrompt))
            return ToolInstructions;

        return agentPrompt + "\n\n" + ToolInstructions;
    }

    private const string ToolInstructions = """
        ## Tool Calls

        You can request actions by emitting one or more tool-call blocks in your response.
        Each block must be on its own line with this exact format:

        [TOOL_CALL:<unique_id>] {"actionType":"<ActionType>","resourceId":"<guid_or_null>"}

        Supported actionType values:
        - UnsafeExecuteAsDangerousShell (include "dangerousShellType":"Bash|PowerShell|CommandPrompt|Git")
        - ExecuteAsSafeShell (include "safeShellType":"Mk8Shell")
        - AccessLocalInfoStore, AccessExternalInfoStore
        - AccessWebsite, QuerySearchEngine, AccessContainer
        - ManageAgent, EditTask, AccessSkill
        - CreateSubAgent, CreateContainer, RegisterInfoStore, EditAnyTask

        For per-resource actions, include "resourceId" with the target resource GUID.
        For global actions, omit "resourceId" or set it to null.

        After you emit tool calls, the system will execute them and reply with results:
        [TOOL_RESULT:<id>] status=Completed result=...
        [TOOL_RESULT:<id>] status=Denied error=...
        [TOOL_RESULT:<id>] status=AwaitingApproval

        When a result is AwaitingApproval, the action requires explicit user
        approval before it can proceed. In that case you MUST:
        1. Tell the user which action needs their approval and why.
        2. Do NOT emit further tool calls — the conversation will pause until
           the user approves or denies the pending job(s).

        When a result is Denied, explain to the user that the action was not
        permitted and suggest alternatives if possible.

        Use completed results to formulate your final response to the user.
        Do NOT include [TOOL_CALL:...] blocks in your final answer to the user.
        """;

    // ═══════════════════════════════════════════════════════════════
    // Internal types
    // ═══════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ParsedToolCall(
        string CallId,
        AgentActionType ActionType,
        Guid? ResourceId,
        DangerousShellType? DangerousShellType,
        SafeShellType? SafeShellType);

    private sealed class ToolCallPayload
    {
        public string? ActionType { get; set; }
        public Guid? ResourceId { get; set; }
        public string? DangerousShellType { get; set; }
        public string? SafeShellType { get; set; }
    }
}
