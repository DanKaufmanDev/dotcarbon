using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Http;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FetchArgs))]
[JsonSerializable(typeof(FetchResponse))]
[JsonSerializable(typeof(HttpOptions))]
internal partial class HttpJsonContext : JsonSerializerContext;
