using System.Text.Json.Serialization;

namespace DotCarbon.Host.Android;

/// <summary>Payloads the Android host emits to the frontend (AOT-safe, source-generated).</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
internal partial class CarbonHostJsonContext : JsonSerializerContext;
