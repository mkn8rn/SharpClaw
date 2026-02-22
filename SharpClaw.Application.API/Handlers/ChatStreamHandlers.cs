using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using SharpClaw.Application.API.Routing;
using SharpClaw.Application.Services;
using SharpClaw.Contracts.DTOs.Chat;

namespace SharpClaw.Application.API.Handlers;

/// <summary>
/// SSE streaming endpoint for chat. Streams <see cref="ChatStreamEvent"/>
/// items as server-sent events. Approval requests pause the stream;
/// the client must POST to the companion approve endpoint to continue.
/// </summary>
[RouteGroup("/channels/{id:guid}/chat")]
public static class ChatStreamHandlers
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Shared pending-approval store keyed by job ID. The SSE stream
    /// registers a TCS here; the companion POST resolves it.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> PendingApprovals = new();

    /// <summary>
    /// <c>POST /channels/{id}/chat/stream</c> — streams the response as SSE.
    /// <para>
    /// When a job needs approval, emits an <c>approval_required</c> event
    /// and waits for the client to POST to
    /// <c>/channels/{id}/chat/stream/approve/{jobId}</c> with a JSON body
    /// <c>{ "approved": true|false }</c>.
    /// </para>
    /// </summary>
    [MapPost("stream")]
    public static async Task StreamChat(
        HttpContext context,
        Guid id,
        ChatRequest request,
        ChatService chatService)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        async Task<bool> ApprovalCallback(
            Contracts.DTOs.AgentActions.AgentJobResponse job, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingApprovals[job.Id] = tcs;

            using var reg = ct.Register(() => tcs.TrySetResult(false));
            try
            {
                return await tcs.Task;
            }
            finally
            {
                PendingApprovals.TryRemove(job.Id, out _);
            }
        }

        try
        {
            await foreach (var evt in chatService.SendMessageStreamAsync(
                id, request, ApprovalCallback, context.RequestAborted))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                var eventName = evt.Type.ToString();
                await context.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n",
                    context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — clean up any pending approvals
        }
    }

    /// <summary>
    /// <c>POST /channels/{id}/chat/stream/approve/{jobId}</c> — resolves
    /// a pending inline approval during a streaming chat session.
    /// </summary>
    [MapPost("stream/approve/{jobId:guid}")]
    public static IResult ResolveApproval(
        HttpContext context,
        Guid id,
        Guid jobId,
        ChatStreamApprovalRequest approval)
    {
        if (PendingApprovals.TryRemove(jobId, out var tcs))
        {
            tcs.TrySetResult(approval.Approved);
            return Results.Ok();
        }

        return Results.NotFound("No pending approval for this job.");
    }
}

public sealed record ChatStreamApprovalRequest(bool Approved);
