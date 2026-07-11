using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Store;

[JsonSerializable(typeof(StoreKeyArgs))]
[JsonSerializable(typeof(StoreSetArgs))]
[JsonSerializable(typeof(StoreLoadArgs))]
[JsonSerializable(typeof(StoreEntry))]
[JsonSerializable(typeof(StoreSnapshot))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
internal partial class StoreJsonContext : JsonSerializerContext;
