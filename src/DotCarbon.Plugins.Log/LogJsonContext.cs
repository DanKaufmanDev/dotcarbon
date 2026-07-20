using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Log;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LogArgs))]
[JsonSerializable(typeof(LogRecord))]
[JsonSerializable(typeof(LogOptions))]
internal partial class LogJsonContext : JsonSerializerContext;
