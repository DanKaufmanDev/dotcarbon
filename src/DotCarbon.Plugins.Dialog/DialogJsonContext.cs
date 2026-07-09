using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Dialog;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenFileArgs))]
[JsonSerializable(typeof(SaveFileArgs))]
[JsonSerializable(typeof(OpenFolderArgs))]
[JsonSerializable(typeof(MessageArgs))]
[JsonSerializable(typeof(ConfirmArgs))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class DialogJsonContext : JsonSerializerContext;
