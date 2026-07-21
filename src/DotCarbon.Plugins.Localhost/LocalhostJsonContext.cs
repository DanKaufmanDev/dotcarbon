using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Localhost;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalhostOptions))]
[JsonSerializable(typeof(LocalhostStartArgs))]
[JsonSerializable(typeof(string))]
internal partial class LocalhostJsonContext : JsonSerializerContext;
