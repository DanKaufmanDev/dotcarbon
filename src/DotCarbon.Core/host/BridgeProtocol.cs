using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCarbon.Core.Host;

internal sealed record BridgeMessage(string Id, string Command, JsonElement Payload);

internal sealed record BridgeResponse(
    string Type,
    string Id,
    bool Ok,
    JsonNode? Data);

internal sealed record BridgeEventMessage(
    string Type,
    long Id,
    string Event,
    JsonNode? Payload,
    string? Source);

/// <summary>A streamed message on a channel (Task 4.1). <see cref="Id"/> is the frontend channel id.</summary>
internal sealed record BridgeChannelMessage(
    string Type,
    long Id,
    JsonNode? Message);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeResponse))]
[JsonSerializable(typeof(BridgeEventMessage))]
[JsonSerializable(typeof(BridgeChannelMessage))]
[JsonSerializable(typeof(JsonNode))]
internal partial class CarbonCoreJsonContext : JsonSerializerContext;
