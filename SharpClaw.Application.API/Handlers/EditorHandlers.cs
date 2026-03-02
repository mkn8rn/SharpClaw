using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpClaw.Application.Services;

namespace SharpClaw.Application.API.Handlers;

/// <summary>
/// WebSocket endpoint for IDE extension connections and REST
/// endpoints for querying connected editor sessions.
/// </summary>
public static class EditorHandlers
{
    public static void MapEditorEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/editor");

        group.Map("/ws", HandleWebSocket);
        group.MapGet("/sessions", ListSessions);
    }

    /// <summary>
    /// WebSocket upgrade endpoint. Extensions connect here and send a
    /// registration message, then enter a request/response loop managed
    /// by <see cref="EditorBridgeService"/>.
    /// </summary>
    private static async Task HandleWebSocket(
        HttpContext context,
        EditorBridgeService bridge)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("WebSocket connections only.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await bridge.HandleConnectionAsync(socket, context.RequestAborted);
    }

    /// <summary>
    /// Lists all currently connected editor sessions.
    /// </summary>
    private static IResult ListSessions(EditorBridgeService bridge)
    {
        var connections = bridge.GetConnections();
        var sessions = connections.Select(c => new
        {
            c.SessionId,
            c.EditorType,
            c.EditorVersion,
            c.WorkspacePath,
            IsConnected = c.Socket.State == WebSocketState.Open,
            c.ConnectedAt
        });
        return Results.Ok(sessions);
    }
}
