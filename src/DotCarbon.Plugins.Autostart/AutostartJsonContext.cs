using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Autostart;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AutostartOptions))]
[JsonSerializable(typeof(bool))]
internal partial class AutostartJsonContext : JsonSerializerContext;
