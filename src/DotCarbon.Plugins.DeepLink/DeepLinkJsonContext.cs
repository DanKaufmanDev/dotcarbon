using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.DeepLink;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeepLinkConfig))]
[JsonSerializable(typeof(DeepLinkInfo))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
internal partial class DeepLinkJsonContext : JsonSerializerContext;
