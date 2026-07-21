using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Positioner;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PositionerMoveArgs))]
internal partial class PositionerJsonContext : JsonSerializerContext;
