using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Biometric;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuthenticateArgs))]
[JsonSerializable(typeof(AuthenticateResult))]
[JsonSerializable(typeof(string))]
internal partial class BiometricJsonContext : JsonSerializerContext;
