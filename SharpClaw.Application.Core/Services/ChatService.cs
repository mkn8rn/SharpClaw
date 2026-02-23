using System.Runtime.CompilerServices;
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
        Guid channelId, ChatRequest request, CancellationToken ct = default)
    {
        var channel = await db.Channels
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == channelId, ct)
            ?? throw new ArgumentException($"Channel {channelId} not found.");

        var model = channel.Model;
        var provider = model.Provider;
        var agent = channel.Agent;

        if (string.IsNullOrEmpty(provider.EncryptedApiKey))
            throw new InvalidOperationException("Provider does not have an API key configured.");

        // Build channel history: recent messages + new user message
        var history = await db.ChatMessages
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxHistoryMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatCompletionMessage(m.Role, m.Content))
            .ToListAsync(ct);

        history.Add(new ChatCompletionMessage("user", request.Message));

        var apiKey = ApiKeyEncryptor.Decrypt(provider.EncryptedApiKey, encryptionOptions.Key);
        var client = clientFactory.GetClient(provider.ProviderType, provider.ApiEndpoint);
        var useNativeTools = client.SupportsNativeToolCalling;
        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, useNativeTools);

        using var httpClient = httpClientFactory.CreateClient();

        var loopResult = useNativeTools
            ? await RunNativeToolLoopAsync(
                client, httpClient, apiKey, model.Name, systemPrompt,
                history, agent.Id, channelId, ct)
            : await RunTextToolLoopAsync(
                client, httpClient, apiKey, model.Name, systemPrompt,
                history, agent.Id, channelId, ct);

        // Persist both messages
        var userMessage = new ChatMessageDB
        {
            Role = "user",
            Content = request.Message,
            ChannelId = channelId
        };

        var assistantMessage = new ChatMessageDB
        {
            Role = "assistant",
            Content = loopResult.AssistantContent,
            ChannelId = channelId
        };

        db.ChatMessages.Add(userMessage);
        db.ChatMessages.Add(assistantMessage);
        await db.SaveChangesAsync(ct);

        return new ChatResponse(
            new ChatMessageResponse(userMessage.Role, userMessage.Content, userMessage.CreatedAt),
            new ChatMessageResponse(assistantMessage.Role, assistantMessage.Content, assistantMessage.CreatedAt),
            loopResult.JobResults.Count > 0 ? loopResult.JobResults : null);
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetHistoryAsync(
        Guid channelId, int limit = 50, CancellationToken ct = default)
    {
        return await db.ChatMessages
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageResponse(m.Role, m.Content, m.CreatedAt))
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Streaming chat
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Streams a chat response token-by-token, executing tool calls
    /// inline. When a job requires approval:
    /// <list type="bullet">
    ///   <item>If the session user can approve, emits
    ///         <see cref="ChatStreamEventType.ApprovalRequired"/> and
    ///         calls <paramref name="approvalCallback"/> to get the
    ///         decision (y/n in CLI, bool in API).</item>
    ///   <item>If the session user cannot approve, the job is
    ///         automatically denied and execution continues.</item>
    /// </list>
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> SendMessageStreamAsync(
        Guid channelId,
        ChatRequest request,
        Func<AgentJobResponse, CancellationToken, Task<bool>> approvalCallback,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = await db.Channels
            .Include(c => c.Model).ThenInclude(m => m.Provider)
            .Include(c => c.Agent)
            .FirstOrDefaultAsync(c => c.Id == channelId, ct)
            ?? throw new ArgumentException($"Channel {channelId} not found.");

        var model = channel.Model;
        var provider = model.Provider;
        var agent = channel.Agent;

        if (string.IsNullOrEmpty(provider.EncryptedApiKey))
            throw new InvalidOperationException("Provider does not have an API key configured.");

        var history = await db.ChatMessages
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxHistoryMessages)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatCompletionMessage(m.Role, m.Content))
            .ToListAsync(ct);

        history.Add(new ChatCompletionMessage("user", request.Message));

        var apiKey = ApiKeyEncryptor.Decrypt(provider.EncryptedApiKey, encryptionOptions.Key);
        var client = clientFactory.GetClient(provider.ProviderType, provider.ApiEndpoint);
        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, nativeToolCalling: true);

        using var httpClient = httpClientFactory.CreateClient();

        // Convert history to tool-aware messages
        var messages = new List<ToolAwareMessage>(history.Count);
        foreach (var msg in history)
            messages.Add(new ToolAwareMessage { Role = msg.Role, Content = msg.Content });

        var jobResults = new List<AgentJobResponse>();
        var fullContent = new StringBuilder();
        var rounds = 0;

        while (true)
        {
            // Stream the current round
            ChatCompletionResult? roundResult = null;

            await foreach (var chunk in client.StreamChatCompletionWithToolsAsync(
                httpClient, apiKey, model.Name, systemPrompt, messages, Mk8ShellTools, ct))
            {
                if (chunk.Delta is not null)
                    yield return ChatStreamEvent.TextDelta(chunk.Delta);

                if (chunk.IsFinished)
                    roundResult = chunk.Finished;
            }

            if (roundResult is null)
                break;

            fullContent.Append(roundResult.Content ?? "");

            if (!roundResult.HasToolCalls || ++rounds > MaxToolCallRounds)
                break;

            // Record assistant turn with tool calls
            messages.Add(ToolAwareMessage.AssistantWithToolCalls(
                roundResult.ToolCalls, roundResult.Content));

            // Reset content for next round (tool results will produce new text)
            fullContent.Clear();

            foreach (var tc in roundResult.ToolCalls)
            {
                var parsed = ParseNativeToolCall(tc);
                if (parsed is null)
                {
                    messages.Add(ToolAwareMessage.ToolResult(tc.Id,
                        "Error: unrecognized tool or malformed arguments."));
                    continue;
                }

                var resourceId = await ResolveContainerIdAsync(parsed, ct);

                var jobRequest = new SubmitAgentJobRequest(
                    ActionType: AgentActionType.ExecuteAsSafeShell,
                    ResourceId: resourceId,
                    CallerAgentId: agent.Id,
                    SafeShellType: SafeShellType.Mk8Shell,
                    ScriptJson: parsed.ScriptJson,
                    ChannelId: channelId);

                var jobResponse = await jobService.SubmitAsync(agent.Id, jobRequest, ct);

                // ── Inline approval ───────────────────────────────
                if (jobResponse.Status == AgentJobStatus.AwaitingApproval)
                {
                    // Check if the session user CAN approve
                    var canApprove = await CanSessionUserApproveAsync(
                        agent.Id, jobRequest.ActionType, resourceId, ct);

                    if (canApprove)
                    {
                        yield return ChatStreamEvent.NeedsApproval(jobResponse);

                        var approved = await approvalCallback(jobResponse, ct);

                        if (approved)
                        {
                            jobResponse = await jobService.ApproveAsync(jobResponse.Id, new ApproveAgentJobRequest(), ct)
                                ?? jobResponse;
                        }
                        else
                        {
                            jobResponse = await jobService.CancelAsync(jobResponse.Id, ct)
                                ?? jobResponse;
                        }
                    }
                    else
                    {
                        // Auto-deny: user cannot approve
                        jobResponse = await jobService.CancelAsync(jobResponse.Id, ct)
                            ?? jobResponse;
                    }

                    yield return ChatStreamEvent.ApprovalDecision(jobResponse);
                }
                else
                {
                    yield return ChatStreamEvent.ToolStart(jobResponse);
                }

                jobResults.Add(jobResponse);

                var resultContent =
                    $"status={jobResponse.Status}" +
                    (jobResponse.ResultData is not null ? $" result={jobResponse.ResultData}" : "") +
                    (jobResponse.ErrorLog is not null ? $" error={jobResponse.ErrorLog}" : "");

                messages.Add(ToolAwareMessage.ToolResult(tc.Id, resultContent));
            }
        }

        // Persist both messages
        var assistantContent = fullContent.ToString();

        var userMessage = new ChatMessageDB
        {
            Role = "user",
            Content = request.Message,
            ChannelId = channelId
        };

        var assistantMessage = new ChatMessageDB
        {
            Role = "assistant",
            Content = assistantContent,
            ChannelId = channelId
        };

        db.ChatMessages.Add(userMessage);
        db.ChatMessages.Add(assistantMessage);
        await db.SaveChangesAsync(ct);

        yield return ChatStreamEvent.Complete(new ChatResponse(
            new ChatMessageResponse(userMessage.Role, userMessage.Content, userMessage.CreatedAt),
            new ChatMessageResponse(assistantMessage.Role, assistantMessage.Content, assistantMessage.CreatedAt),
            jobResults.Count > 0 ? jobResults : null));
    }

    /// <summary>
    /// Checks whether the current session user has sufficient authority
    /// to approve the given action — i.e. their own permission check
    /// would return <see cref="ClearanceVerdict.Approved"/>.
    /// </summary>
    private async Task<bool> CanSessionUserApproveAsync(
        Guid agentId, AgentActionType actionType, Guid? resourceId,
        CancellationToken ct)
    {
        var userId = jobService.GetSessionUserId();
        if (userId is null) return false;

        var caller = new ActionCaller(UserId: userId);
        var result = await jobService.CheckPermissionAsync(
            agentId, actionType, resourceId, caller, ct);

        return result.Verdict == ClearanceVerdict.Approved;
    }

    /// <summary>
    /// Resolves the container GUID from a <see cref="ParsedToolCall"/>.
    /// If the model provided a valid GUID it is used as-is; otherwise the
    /// container is looked up by <c>SandboxName</c>.
    /// </summary>
    private async Task<Guid?> ResolveContainerIdAsync(
        ParsedToolCall parsed, CancellationToken ct)
    {
        if (parsed.ResourceId.HasValue)
            return parsed.ResourceId;

        if (parsed.SandboxId is not null)
        {
            var container = await db.Containers
                .FirstOrDefaultAsync(c => c.SandboxName == parsed.SandboxId, ct);
            return container?.Id;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool-call loop implementations
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Uses native provider function calling. The provider returns structured
    /// tool calls that are dispatched through the job pipeline, with results
    /// fed back as <c>tool</c>-role messages.
    /// </summary>
    private async Task<ToolLoopResult> RunNativeToolLoopAsync(
        IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        string modelName,
        string? systemPrompt,
        IReadOnlyList<ChatCompletionMessage> dbHistory,
        Guid agentId,
        Guid channelId,
        CancellationToken ct)
    {
        var messages = new List<ToolAwareMessage>(dbHistory.Count);
        foreach (var msg in dbHistory)
            messages.Add(new ToolAwareMessage { Role = msg.Role, Content = msg.Content });

        var jobResults = new List<AgentJobResponse>();
        var rounds = 0;

        while (true)
        {
            var result = await client.ChatCompletionWithToolsAsync(
                httpClient, apiKey, modelName, systemPrompt, messages, Mk8ShellTools, ct);

            if (!result.HasToolCalls || ++rounds > MaxToolCallRounds)
                return new ToolLoopResult(result.Content ?? "", jobResults);

            // Record assistant turn with tool calls
            messages.Add(ToolAwareMessage.AssistantWithToolCalls(result.ToolCalls, result.Content));

            var anyAwaitingApproval = false;

            foreach (var tc in result.ToolCalls)
            {
                var parsed = ParseNativeToolCall(tc);
                if (parsed is null)
                {
                    messages.Add(ToolAwareMessage.ToolResult(tc.Id,
                        "Error: unrecognized tool or malformed arguments."));
                    continue;
                }

                var resourceId = await ResolveContainerIdAsync(parsed, ct);

                var jobRequest = new SubmitAgentJobRequest(
                    ActionType: AgentActionType.ExecuteAsSafeShell,
                    ResourceId: resourceId,
                    CallerAgentId: agentId,
                    SafeShellType: SafeShellType.Mk8Shell,
                    ScriptJson: parsed.ScriptJson,
                    ChannelId: channelId);

                var jobResponse = await jobService.SubmitAsync(agentId, jobRequest, ct);
                jobResults.Add(jobResponse);

                var resultContent =
                    $"status={jobResponse.Status}" +
                    (jobResponse.ResultData is not null ? $" result={jobResponse.ResultData}" : "") +
                    (jobResponse.ErrorLog is not null ? $" error={jobResponse.ErrorLog}" : "");

                messages.Add(ToolAwareMessage.ToolResult(tc.Id, resultContent));

                if (jobResponse.Status == AgentJobStatus.AwaitingApproval)
                    anyAwaitingApproval = true;
            }

            if (anyAwaitingApproval)
            {
                var finalResult = await client.ChatCompletionWithToolsAsync(
                    httpClient, apiKey, modelName, systemPrompt, messages, Mk8ShellTools, ct);
                return new ToolLoopResult(finalResult.Content ?? "", jobResults);
            }
        }
    }

    /// <summary>
    /// Text-based fallback for providers that lack native tool calling.
    /// The model is instructed via the system prompt to emit
    /// <c>[TOOL_CALL:...]</c> markers which are parsed post-hoc.
    /// </summary>
    private async Task<ToolLoopResult> RunTextToolLoopAsync(
        IProviderApiClient client,
        HttpClient httpClient,
        string apiKey,
        string modelName,
        string systemPrompt,
        List<ChatCompletionMessage> history,
        Guid agentId,
        Guid channelId,
        CancellationToken ct)
    {
        var jobResults = new List<AgentJobResponse>();
        string assistantContent;
        var rounds = 0;

        while (true)
        {
            assistantContent = await client.ChatCompletionAsync(
                httpClient, apiKey, modelName, systemPrompt, history, ct);

            var toolCalls = ParseToolCalls(assistantContent);
            if (toolCalls.Count == 0 || ++rounds > MaxToolCallRounds)
                break;

            history.Add(new ChatCompletionMessage("assistant", assistantContent));

            var toolResultBuilder = new StringBuilder();
            var anyAwaitingApproval = false;

            foreach (var call in toolCalls)
            {
                var resourceId = await ResolveContainerIdAsync(call, ct);

                var jobRequest = new SubmitAgentJobRequest(
                    ActionType: AgentActionType.ExecuteAsSafeShell,
                    ResourceId: resourceId,
                    CallerAgentId: agentId,
                    SafeShellType: SafeShellType.Mk8Shell,
                    ScriptJson: call.ScriptJson,
                    ChannelId: channelId);

                var jobResponse = await jobService.SubmitAsync(agentId, jobRequest, ct);
                jobResults.Add(jobResponse);

                toolResultBuilder.AppendLine(
                    $"[TOOL_RESULT:{call.CallId}] status={jobResponse.Status}" +
                    (jobResponse.ResultData is not null ? $" result={jobResponse.ResultData}" : "") +
                    (jobResponse.ErrorLog is not null ? $" error={jobResponse.ErrorLog}" : ""));

                if (jobResponse.Status == AgentJobStatus.AwaitingApproval)
                    anyAwaitingApproval = true;
            }

            history.Add(new ChatCompletionMessage("user", toolResultBuilder.ToString()));

            if (anyAwaitingApproval)
            {
                assistantContent = await client.ChatCompletionAsync(
                    httpClient, apiKey, modelName, systemPrompt, history, ct);
                break;
            }
        }

        assistantContent = StripToolCallBlocks(assistantContent);
        return new ToolLoopResult(assistantContent, jobResults);
    }

    /// <summary>
    /// Parses a native <see cref="ChatToolCall"/> into the internal
    /// <see cref="ParsedToolCall"/> representation. Returns <see langword="null"/>
    /// if the tool name is unrecognized or the arguments are malformed.
    /// </summary>
    private static ParsedToolCall? ParseNativeToolCall(ChatToolCall toolCall)
    {
        if (toolCall.Name != "execute_mk8_shell")
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<ToolCallPayload>(toolCall.ArgumentsJson, JsonOptions);
            if (payload is null) return null;

            Guid? resourceId = Guid.TryParse(payload.ResourceId, out var rid) ? rid : null;

            return new ParsedToolCall(
                toolCall.Id,
                resourceId,
                payload.SandboxId,
                payload.Script is { } script ? script.GetRawText() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Tool-call parsing (text-based fallback)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches the <c>[TOOL_CALL:id]</c> marker emitted by the model.
    /// The JSON payload that follows is extracted separately via
    /// <see cref="ExtractJsonObject"/> to support nested objects
    /// (e.g. mk8.shell scripts).
    /// </summary>
    [GeneratedRegex(
        @"\[TOOL_CALL:(?<id>[^\]]+)\]\s*",
        RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ToolCallMarkerPattern();

    /// <summary>
    /// Extracts a balanced JSON object starting at <paramref name="startIndex"/>.
    /// Returns <see langword="null"/> if the character at that position is not
    /// <c>{</c> or if the braces never balance.
    /// </summary>
    private static string? ExtractJsonObject(string text, int startIndex)
    {
        if (startIndex >= text.Length || text[startIndex] != '{')
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[startIndex..(i + 1)];
            }
        }

        return null;
    }

    private static IReadOnlyList<ParsedToolCall> ParseToolCalls(string content)
    {
        var markers = ToolCallMarkerPattern().Matches(content);
        if (markers.Count == 0)
            return [];

        var calls = new List<ParsedToolCall>(markers.Count);
        foreach (Match marker in markers)
        {
            var callId = marker.Groups["id"].Value;
            var jsonStart = marker.Index + marker.Length;
            var json = ExtractJsonObject(content, jsonStart);

            if (json is null)
                continue;

            try
            {
                var payload = JsonSerializer.Deserialize<ToolCallPayload>(json, JsonOptions);
                if (payload is not null)
                {
                    Guid? resourceId = Guid.TryParse(payload.ResourceId, out var rid) ? rid : null;

                    calls.Add(new ParsedToolCall(
                        callId,
                        resourceId,
                        payload.SandboxId,
                        payload.Script is { } script
                            ? script.GetRawText()
                            : null));
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
    {
        var markers = ToolCallMarkerPattern().Matches(content);
        if (markers.Count == 0)
            return content;

        var sb = new StringBuilder(content);
        for (var i = markers.Count - 1; i >= 0; i--)
        {
            var marker = markers[i];
            var jsonStart = marker.Index + marker.Length;
            var json = ExtractJsonObject(content, jsonStart);
            var endIndex = json is not null
                ? jsonStart + json.Length
                : marker.Index + marker.Length;
            sb.Remove(marker.Index, endIndex - marker.Index);
        }

        return sb.ToString().Trim();
    }

    // ═══════════════════════════════════════════════════════════════
    // System prompt
    // ═══════════════════════════════════════════════════════════════

    private static string BuildSystemPrompt(string? agentPrompt, bool nativeToolCalling)
    {
        var suffix = nativeToolCalling ? NativeToolSystemSuffix : ToolInstructions;

        if (string.IsNullOrEmpty(agentPrompt))
            return suffix;

        return agentPrompt + "\n\n" + suffix;
    }

    private const string ToolInstructions = """
        ## Tool Calls — mk8.shell

        You can execute commands inside a sandbox by emitting one or more
        tool-call blocks in your response. Each block must be on its own line:

        [TOOL_CALL:<unique_id>] {"resourceId":"<container-guid>","sandboxId":"<name>","script":{...}}

        • resourceId — the GUID of the container resource to execute against.
        • sandboxId  — the mk8.shell sandbox name (resolved from the registry).
        • script     — an mk8.shell script object (see reference below).

        After you emit tool calls, the system executes them and replies with:
        [TOOL_RESULT:<id>] status=Completed result=...
        [TOOL_RESULT:<id>] status=Denied error=...
        [TOOL_RESULT:<id>] status=AwaitingApproval

        When a result is AwaitingApproval, the action requires explicit user
        approval before it can proceed. You MUST:
        1. Tell the user which action needs their approval and why.
        2. Do NOT emit further tool calls until the user approves or denies.

        When a result is Denied, explain that the action was not permitted
        and suggest alternatives if possible.

        Use completed results to formulate your final response to the user.
        Do NOT include [TOOL_CALL:...] blocks in your final answer.

        ---

        ### mk8.shell script reference

        You submit JSON scripts. The server compiles and executes them inside
        a sandboxed environment. There is no real shell — no eval, no pipes,
        no chaining, no shell expansion. Arguments are structured arrays.

        Every command executes inside the named sandbox. The server resolves
        it to a local directory, verifies its cryptographically signed
        environment, executes the command in an isolated task container, then
        disposes all state. Nothing transfers between commands.

        You CANNOT register, create, or manage sandboxes. If a sandbox does
        not exist, tell the user to register it using mk8.shell.startup.

        Script format:
        {
          "operations": [
            { "verb": "...", "args": ["..."] }
          ],
          "options": { ... },
          "cleanup": [ ... ]
        }

        Every operation needs "verb" and "args". All other fields are optional.

        #### Verbs

        Files:
          FileRead [path], FileWrite [path, content],
          FileAppend [path, content], FileDelete [path],
          FileExists [path], FileList [path, pattern?],
          FileCopy [src, dst], FileMove [src, dst],
          FileHash [path, algorithm?] (sha256 default, sha512, md5)

        Structured edits:
          FileTemplate [outputPath]  — requires "template" field
          FilePatch    [targetPath]  — requires "patches" field

        Batch (max 64 entries each):
          FileWriteMany  [p1, c1, p2, c2...] pairs of path+content
          FileCopyMany   [s1, d1, s2, d2...] pairs of src+dst
          FileDeleteMany [p1, p2, p3...]

        Directories:
          DirCreate [path], DirDelete [path], DirList [path],
          DirExists [path], DirTree [path, depth?] (depth 1–5, default 3)

        Process:
          ProcRun [binary, arg, arg...] — strict command-template whitelist

        Git (via ProcRun whitelist):
          Read-only: status, log, diff, branch, remote, ls-files, tag, describe
          Write: add, commit, stash, checkout, switch
          Protected branches (main, master, develop, staging, production,
          live, release/*, trunk) are BANNED.

        HTTP:
          HttpGet [url], HttpPost [url, body?],
          HttpPut [url, body?], HttpDelete [url]

        Text:
          TextRegex [input, pattern] (2 s timeout),
          TextReplace [input, old, new],
          JsonParse [input], JsonQuery [input, jsonpath]

        Environment (read-only, allowlist only):
          EnvGet [name]

        System info (no args):
          SysWhoAmI, SysPwd, SysHostname, SysUptime, SysDate (UTC)

        Control flow (expanded at compile time):
          ForEach [] — requires "forEach" field
          If []      — requires "if" field

        Composition:
          Include [fragmentId] — inline admin-approved fragment

        #### Variables

        Resolved at compile time, not shell env vars:
          $WORKSPACE — sandbox root directory
          $CWD       — working directory (defaults to sandbox root)
          $USER      — OS username
          $PREV      — stdout of previous step (only when pipeStepOutput: true)

        Sandbox signed env vars are also available automatically.
        Use in args: { "args": ["$WORKSPACE/src/app.ts"] }
        $PREV is always empty when pipeStepOutput is false (default).

        #### Named captures

        Any step can capture its stdout:
        { "verb": "ProcRun", "args": ["dotnet","build"], "captureAs": "BUILD_OUT" }
        { "verb": "FileWrite", "args": ["$WORKSPACE/log.txt","$BUILD_OUT"] }

        Max 16 captures per script. Cannot reuse names. Cannot override
        WORKSPACE, CWD, USER, PREV, ITEM, INDEX.
        Captures from process-spawning steps are blocked in ProcRun args.

        #### Per-step fields

        maxRetries (int), stepTimeout (TimeSpan e.g. "00:02:00"),
        label (string, unique, alphanumeric/hyphens/underscores, max 64),
        onFailure ("goto:<label>" — forward jumps only),
        captureAs (string), template (object), patches (array)

        #### Options

        maxRetries (0), retryDelay ("00:00:02" — doubles each attempt),
        stepTimeout ("00:00:30"), scriptTimeout ("00:05:00"),
        failureMode ("StopOnFirstError" | "ContinueOnError" | "StopAndCleanup"),
        maxOutputBytes (1048576), maxErrorBytes (262144),
        pipeStepOutput (false)

        #### Cleanup

        When failureMode is "StopAndCleanup", add a "cleanup" array.
        These run after failure with best-effort semantics:
        { "cleanup": [{ "verb": "DirDelete", "args": ["$WORKSPACE/tmp"] }] }

        #### FileTemplate

        { "verb": "FileTemplate", "args": ["$WORKSPACE/config.json"],
          "template": {
            "source": "$WORKSPACE/templates/config.template.json",
            "values": { "DB_HOST": "db.internal", "PORT": "5432" }
          } }

        Replaces {{KEY}} placeholders. Max 64 keys. No $ in values.

        #### Security constraints

        - No interpreters (bash, cmd, powershell).
        - ProcRun uses a strict command-template whitelist.
        - Paths are workspace-relative only (no "..").
        - URL sanitization (SSRF, private IP, metadata, credentials blocked).
        - Env allowlist blocks KEY/SECRET/TOKEN/PASSWORD/APIKEY.
        - Structurally impossible: sudo, pipes, chaining, redirection,
          backgrounding, interpreter invocation.
        """;

    private const string NativeToolSystemSuffix = """
        ## mk8.shell Tool Results

        After calling execute_mk8_shell, results indicate the job status:
        - status=Completed result=<output> — the command succeeded.
        - status=Denied error=<reason> — the command was blocked by permissions.
        - status=AwaitingApproval — the command requires user approval.

        When AwaitingApproval:
        1. Tell the user which action needs approval and why.
        2. Do NOT call further tools until the user approves or denies.

        When Denied, explain the permission issue and suggest alternatives.

        You CANNOT register, create, or manage sandboxes. If a sandbox does
        not exist, tell the user to register it using mk8.shell.startup.
        """;

    private const string Mk8ShellToolDescription = """
        Execute commands inside a sandboxed mk8.shell environment.

        You submit JSON scripts. The server compiles and executes them in an
        isolated workspace. There is no real shell — no eval, pipes, chaining,
        or shell expansion. Arguments are structured arrays.

        Every command executes inside the named sandbox. The server resolves
        it to a local directory, verifies its cryptographically signed
        environment, and executes in an isolated task container.

        Script format: { "operations": [{ "verb": "...", "args": ["..."] }], "options": {...}, "cleanup": [...] }

        Verbs:
        Files: FileRead [path], FileWrite [path, content], FileAppend [path, content],
          FileDelete [path], FileExists [path], FileList [path, pattern?],
          FileCopy [src, dst], FileMove [src, dst], FileHash [path, algorithm?]
        Structured edits: FileTemplate [outputPath] (requires "template" field),
          FilePatch [targetPath] (requires "patches" field)
        Batch (max 64): FileWriteMany [p1,c1,p2,c2...], FileCopyMany [s1,d1,s2,d2...],
          FileDeleteMany [p1,p2,...]
        Directories: DirCreate [path], DirDelete [path], DirList [path],
          DirExists [path], DirTree [path, depth?] (1–5, default 3)
        Process: ProcRun [binary, arg...] — strict command-template whitelist
        Git (via ProcRun): status, log, diff, branch, remote, ls-files, tag, describe,
          add, commit, stash, checkout, switch. Protected branches BANNED.
        HTTP: HttpGet [url], HttpPost [url, body?], HttpPut [url, body?], HttpDelete [url]
        Text: TextRegex [input, pattern], TextReplace [input, old, new],
          JsonParse [input], JsonQuery [input, jsonpath]
        Env (read-only allowlist): EnvGet [name]
        System (no args): SysWhoAmI, SysPwd, SysHostname, SysUptime, SysDate
        Control flow: ForEach [] ("forEach" field), If [] ("if" field)
        Composition: Include [fragmentId]

        Variables (compile-time): $WORKSPACE, $CWD, $USER, $PREV (when pipeStepOutput: true).
        Named captures: { "captureAs": "BUILD_OUT" } — max 16, blocked in ProcRun args.

        Per-step fields: maxRetries, stepTimeout, label, onFailure ("goto:<label>"),
          captureAs, template, patches.
        Options: maxRetries (0), retryDelay ("00:00:02"), stepTimeout ("00:00:30"),
          scriptTimeout ("00:05:00"), failureMode, maxOutputBytes, maxErrorBytes,
          pipeStepOutput (false).
        Cleanup: runs after failure when failureMode is "StopAndCleanup".
        FileTemplate: replaces {{KEY}} placeholders. Max 64 keys. No $ in values.

        Security: no interpreters, ProcRun whitelist only, workspace-relative paths,
        URL sanitization, env allowlist. Structurally impossible: sudo, pipes,
        chaining, redirection, backgrounding.
        """;

    private static readonly JsonElement Mk8ShellToolSchema = BuildMk8ShellToolSchema();

    private static readonly IReadOnlyList<ChatToolDefinition> Mk8ShellTools =
        [new("execute_mk8_shell", Mk8ShellToolDescription, Mk8ShellToolSchema)];

    private static JsonElement BuildMk8ShellToolSchema()
    {
        using var doc = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "resourceId": {
                        "type": "string",
                        "description": "The GUID of the container resource to execute against."
                    },
                    "sandboxId": {
                        "type": "string",
                        "description": "The mk8.shell sandbox name (resolved from the local registry)."
                    },
                    "script": {
                        "type": "object",
                        "description": "The mk8.shell script object.",
                        "properties": {
                            "operations": {
                                "type": "array",
                                "description": "Ordered list of operations. Each needs verb and args.",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "verb": { "type": "string" },
                                        "args": { "type": "array", "items": { "type": "string" } }
                                    },
                                    "required": ["verb", "args"]
                                }
                            },
                            "options": { "type": "object" },
                            "cleanup": {
                                "type": "array",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "verb": { "type": "string" },
                                        "args": { "type": "array", "items": { "type": "string" } }
                                    },
                                    "required": ["verb", "args"]
                                }
                            }
                        },
                        "required": ["operations"]
                    }
                },
                "required": ["resourceId", "sandboxId", "script"]
            }
            """);
        return doc.RootElement.Clone();
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal types
    // ═══════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ParsedToolCall(
        string CallId,
        Guid? ResourceId,
        string? SandboxId,
        string? ScriptJson);

    private sealed class ToolCallPayload
    {
        public string? ResourceId { get; set; }
        public string? SandboxId { get; set; }
        public JsonElement? Script { get; set; }
    }

    private readonly record struct ToolLoopResult(
        string AssistantContent,
        List<AgentJobResponse> JobResults);
}
