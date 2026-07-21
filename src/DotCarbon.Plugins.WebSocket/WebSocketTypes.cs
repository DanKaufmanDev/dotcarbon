namespace DotCarbon.Plugins.WebSocket;

/// <summary>Open a client websocket connection to <c>Url</c> (ws:// or wss://).</summary>
public record WsConnectArgs(string Url);

/// <summary>Send a text frame on a connection.</summary>
public record WsSendArgs(long Id, string Data);

/// <summary>Close a connection.</summary>
public record WsDisconnectArgs(long Id);

/// <summary>
/// A frame received on connection <c>Id</c>, delivered via the <c>websocket:message</c> event.
/// <c>Type</c> is "text", "binary" (base64 <c>Data</c>), "close", or "error".
/// </summary>
public record WsMessage(long Id, string Type, string Data);
