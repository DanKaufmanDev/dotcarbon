using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Shell;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ExecuteArgs))]
[JsonSerializable(typeof(OpenArgs))]
[JsonSerializable(typeof(ShellOptions))]
[JsonSerializable(typeof(ShellResult))]
internal partial class ShellJsonContext : JsonSerializerContext;
