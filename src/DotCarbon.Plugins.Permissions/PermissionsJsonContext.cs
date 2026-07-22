using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Permissions;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PermissionArgs))]
[JsonSerializable(typeof(string))]
internal partial class PermissionsJsonContext : JsonSerializerContext;
