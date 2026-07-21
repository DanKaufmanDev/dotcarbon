using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.PersistedScope;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScopeGrant))]
[JsonSerializable(typeof(PersistedScopeOptions))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
internal partial class PersistedScopeJsonContext : JsonSerializerContext;
