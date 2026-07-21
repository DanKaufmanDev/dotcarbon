using System.Net.WebSockets;
using System.Text;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.WebSocket;

/// <summary>
/// Client websockets proxied through the backend (Task 6.6/6.7). Connections run in C# — bypassing the
/// webview's mixed-content and CORS limits — and received frames are streamed to the frontend as
/// <c>websocket:message</c> events, mirroring Tauri's websocket plugin.
/// </summary>
[CarbonPlugin("WebSocket", description: "Client websocket connections proxied through the backend.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("websocket:default", "Allow all websocket commands.", Commands = new[] { "websocket:*" })]
[CarbonEvent("websocket:message", "WsMessage", "A frame received from a websocket connection.")]
public partial class WebSocketPlugin : IPlugin
{
    private readonly AppHandle _app;
    private readonly object _gate = new();
    private readonly Dictionary<long, Connection> _connections = new();
    private long _nextId;

    public WebSocketPlugin(AppHandle app) => _app = app;

    public string Namespace => "websocket";

    /// <summary>Open a connection; returns its id. Received frames arrive as websocket:message events.</summary>
    [CarbonCommand("connect")]
    public async Task<long> Connect(WsConnectArgs args)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(args.Url), CancellationToken.None);

        var id = Interlocked.Increment(ref _nextId);
        var cts = new CancellationTokenSource();
        lock (_gate) _connections[id] = new Connection(socket, cts);

        _ = ReceiveLoop(id, socket, cts.Token);
        return id;
    }

    /// <summary>Send a text frame on a connection.</summary>
    [CarbonCommand("send")]
    public Task Send(WsSendArgs args)
    {
        var socket = Socket(args.Id);
        return socket.SendAsync(
            Encoding.UTF8.GetBytes(args.Data), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    /// <summary>Close a connection.</summary>
    [CarbonCommand("disconnect")]
    public async Task Disconnect(WsDisconnectArgs args)
    {
        Connection? connection;
        lock (_gate) _connections.Remove(args.Id, out connection);
        if (connection is null) return;
        await connection.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Connection[] connections;
        lock (_gate)
        {
            connections = [.. _connections.Values];
            _connections.Clear();
        }
        foreach (var connection in connections)
            await connection.DisposeAsync();
    }

    private async Task ReceiveLoop(long id, ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Emit(new WsMessage(id, "close", string.Empty));
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var bytes = message.ToArray();
                await Emit(result.MessageType == WebSocketMessageType.Text
                    ? new WsMessage(id, "text", Encoding.UTF8.GetString(bytes))
                    : new WsMessage(id, "binary", Convert.ToBase64String(bytes)));
            }
        }
        catch (OperationCanceledException)
        {
            // disconnect() cancelled the loop.
        }
        catch (Exception ex)
        {
            await Emit(new WsMessage(id, "error", ex.Message));
        }
    }

    private Task Emit(WsMessage message) =>
        _app.EmitAsync(new CarbonEventName<WsMessage>("websocket:message"), message, WebSocketJsonContext.Default.WsMessage);

    private ClientWebSocket Socket(long id)
    {
        lock (_gate)
        {
            return _connections.TryGetValue(id, out var connection)
                ? connection.Socket
                : throw new InvalidOperationException($"No websocket connection with id {id}.");
        }
    }

    private sealed record Connection(ClientWebSocket Socket, CancellationTokenSource Cts)
    {
        public async ValueTask DisposeAsync()
        {
            Cts.Cancel();
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
            catch
            {
                // Closing a broken socket is best effort.
            }
            Socket.Dispose();
            Cts.Dispose();
        }
    }
}
