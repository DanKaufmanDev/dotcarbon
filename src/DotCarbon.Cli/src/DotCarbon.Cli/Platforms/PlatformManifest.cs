using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCarbon.Cli.Platforms;

/// <summary>
/// Records what Carbon generated for a platform: the CLI version, a hash of the config
/// inputs, and the hash of every managed file at generation time. This is how
/// <c>sync</c>/<c>list</c> tell "up to date" from "config changed" from "manually edited".
/// Lives at <c>.carbon/platforms/&lt;platform&gt;/.carbon-manifest.json</c>.
/// </summary>
internal sealed class PlatformManifest
{
    public const string FileName = ".carbon-manifest.json";

    public string Platform { get; set; } = "";
    public string CarbonVersion { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public string GeneratedAt { get; set; } = "";

    /// <summary>Managed file relative path → content hash at generation time.</summary>
    public Dictionary<string, string> Files { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Path(string platformDir) => System.IO.Path.Combine(platformDir, FileName);

    public static PlatformManifest? Load(string platformDir)
    {
        var path = Path(platformDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<PlatformManifest>(File.ReadAllText(path), JsonOptions); }
        catch { return null; }
    }

    public void Save(string platformDir) =>
        File.WriteAllText(Path(platformDir), JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Content hash, normalized for line endings so CRLF conversion is not "an edit".</summary>
    public static string HashContent(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string HashSignature(string signature) => HashContent(signature);
}
