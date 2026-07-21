using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Cli;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CliOptions))]
[JsonSerializable(typeof(ArgMatches))]
internal partial class CliJsonContext : JsonSerializerContext;
