using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.WebSocket;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WsConnectArgs))]
[JsonSerializable(typeof(WsSendArgs))]
[JsonSerializable(typeof(WsDisconnectArgs))]
[JsonSerializable(typeof(WsMessage))]
[JsonSerializable(typeof(long))]
internal partial class WebSocketJsonContext : JsonSerializerContext;
