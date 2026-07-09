using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Clipboard;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WriteTextArgs))]
[JsonSerializable(typeof(string))]
internal partial class ClipboardJsonContext : JsonSerializerContext;
