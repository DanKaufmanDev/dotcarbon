using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Updater;

[JsonSerializable(typeof(UpdaterStatus))]
[JsonSerializable(typeof(CheckUpdateArgs))]
[JsonSerializable(typeof(UpdateCheckResult))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
internal partial class UpdaterJsonContext : JsonSerializerContext;
