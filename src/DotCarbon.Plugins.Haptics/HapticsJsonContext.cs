using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Haptics;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ImpactArgs))]
[JsonSerializable(typeof(NotificationArgs))]
[JsonSerializable(typeof(VibrateArgs))]
internal partial class HapticsJsonContext : JsonSerializerContext;
