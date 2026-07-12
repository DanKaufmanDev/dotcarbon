using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.GlobalShortcut;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegisterShortcutArgs))]
[JsonSerializable(typeof(ShortcutIdArgs))]
[JsonSerializable(typeof(ShortcutInfo))]
[JsonSerializable(typeof(ShortcutInfo[]))]
[JsonSerializable(typeof(GlobalShortcutPressed))]
[JsonSerializable(typeof(bool))]
internal partial class GlobalShortcutJsonContext : JsonSerializerContext;
