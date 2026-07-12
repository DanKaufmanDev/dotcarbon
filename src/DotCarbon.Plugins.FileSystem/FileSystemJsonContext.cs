using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.FileSystem;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FileSystemOptions))]
[JsonSerializable(typeof(ReadFileArgs))]
[JsonSerializable(typeof(WriteFileArgs))]
[JsonSerializable(typeof(ReadDirArgs))]
[JsonSerializable(typeof(RenameArgs))]
[JsonSerializable(typeof(DeleteArgs))]
[JsonSerializable(typeof(ExistsArgs))]
[JsonSerializable(typeof(DirEntry[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class FileSystemJsonContext : JsonSerializerContext;
