using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.SecureStorage;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SecretArgs))]
[JsonSerializable(typeof(KeyArgs))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SecureStorageJsonContext : JsonSerializerContext;
