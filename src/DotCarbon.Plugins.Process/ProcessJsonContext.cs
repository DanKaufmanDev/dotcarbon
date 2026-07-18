using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Process;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExitArgs))]
[JsonSerializable(typeof(int))]
internal partial class ProcessJsonContext : JsonSerializerContext;
