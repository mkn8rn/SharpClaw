using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SharpClaw.VS2026
{
    /// <summary>
    /// WebSocket client that connects to the SharpClaw editor bridge and
    /// handles action requests by delegating to <see cref="IEditorActionHandler"/>.
    /// </summary>
    public sealed class EditorBridgeClient : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly IEditorActionHandler _handler;
        private CancellationTokenSource _cts;

        public EditorBridgeClient(IEditorActionHandler handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// Connects to the SharpClaw editor bridge, sends registration,
        /// and enters the request/response loop.
        /// </summary>
        public async Task ConnectAsync(
            string url, string editorVersion, string workspacePath,
            CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            await _socket.ConnectAsync(new Uri(url), _cts.Token);

            // Send registration
            var registration = new Dictionary<string, object>
            {
                ["type"] = "register",
                ["editorType"] = "VisualStudio2026",
                ["editorVersion"] = editorVersion,
                ["workspacePath"] = workspacePath
            };

            await SendAsync(registration, _cts.Token);

            // Read the registration acknowledgement
            await ReceiveAsync(_cts.Token);

            // Enter request loop
            while (_socket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var message = await ReceiveAsync(_cts.Token);
                if (message == null) break;

                if (message.Value.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "request")
                {
                    var msg = message.Value.Clone();
                    _ = Task.Run(() => HandleRequestAsync(msg, _cts.Token), _cts.Token);
                }
            }
        }

        private async Task HandleRequestAsync(JsonElement message, CancellationToken ct)
        {
            var requestId = message.GetProperty("requestId").GetGuid();
            var action = message.GetProperty("action").GetString();

            Dictionary<string, object> parameters = null;
            if (message.TryGetProperty("params", out var paramsProp) &&
                paramsProp.ValueKind == JsonValueKind.Object)
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    paramsProp.GetRawText(), JsonOptions);
            }

            try
            {
                var result = await _handler.HandleAsync(action, parameters, ct);
                await SendAsync(new Dictionary<string, object>
                {
                    ["type"] = "response",
                    ["requestId"] = requestId,
                    ["success"] = true,
                    ["data"] = result
                }, ct);
            }
            catch (Exception ex)
            {
                await SendAsync(new Dictionary<string, object>
                {
                    ["type"] = "response",
                    ["requestId"] = requestId,
                    ["success"] = false,
                    ["error"] = ex.Message
                }, ct);
            }
        }

        private async Task SendAsync(object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, true, ct);
        }

        private async Task<JsonElement?> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            var result = await _socket.ReceiveAsync(
                new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using (var doc = JsonDocument.Parse(json))
            {
                return doc.RootElement.Clone();
            }
        }

        public void Dispose()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _socket.Dispose();
        }
    }
}
