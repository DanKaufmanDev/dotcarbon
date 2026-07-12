using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Os;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OsInfo))]
[JsonSerializable(typeof(string))]
internal partial class OsJsonContext : JsonSerializerContext;
