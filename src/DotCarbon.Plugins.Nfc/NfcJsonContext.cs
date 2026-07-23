using System.Text.Json.Serialization;

namespace DotCarbon.Plugins.Nfc;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NfcTag))]
[JsonSerializable(typeof(NfcRecord))]
[JsonSerializable(typeof(ScanArgs))]
[JsonSerializable(typeof(string))]
internal partial class NfcJsonContext : JsonSerializerContext;
