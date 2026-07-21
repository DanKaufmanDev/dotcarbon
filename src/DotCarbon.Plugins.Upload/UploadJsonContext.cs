using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Upload;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UploadArgs))]
[JsonSerializable(typeof(DownloadArgs))]
[JsonSerializable(typeof(ProgressPayload))]
[JsonSerializable(typeof(string))]
internal partial class UploadJsonContext : JsonSerializerContext;
