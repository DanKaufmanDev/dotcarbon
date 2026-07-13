using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Updater;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdaterStatus))]
[JsonSerializable(typeof(CheckUpdateArgs))]
[JsonSerializable(typeof(UpdateCheckResult))]
[JsonSerializable(typeof(DownloadUpdateArgs))]
[JsonSerializable(typeof(InstallUpdateArgs))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateDownloadResult))]
[JsonSerializable(typeof(UpdateInstallResult))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
internal partial class UpdaterJsonContext : JsonSerializerContext;
