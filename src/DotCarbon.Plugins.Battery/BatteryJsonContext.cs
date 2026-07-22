using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Battery;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BatteryStatus))]
internal partial class BatteryJsonContext : JsonSerializerContext;
