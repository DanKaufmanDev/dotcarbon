using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Path;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PathArg))]
[JsonSerializable(typeof(PathPartsArgs))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class PathJsonContext : JsonSerializerContext;
