using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.WindowState;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, WindowState>))]
[JsonSerializable(typeof(WindowState))]
[JsonSerializable(typeof(WindowLabelArgs))]
[JsonSerializable(typeof(WindowStateOptions))]
internal partial class WindowStateJsonContext : JsonSerializerContext;
