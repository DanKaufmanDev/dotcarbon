using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Geolocation;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GeolocationPosition))]
[JsonSerializable(typeof(CurrentPositionArgs))]
internal partial class GeolocationJsonContext : JsonSerializerContext;
