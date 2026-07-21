using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Sql;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SqlLoadArgs))]
[JsonSerializable(typeof(SqlQueryArgs))]
[JsonSerializable(typeof(SqlExecuteResult))]
[JsonSerializable(typeof(SqlOptions))]
[JsonSerializable(typeof(List<Dictionary<string, JsonNode?>>))]
internal partial class SqlJsonContext : JsonSerializerContext;
