using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCarbon.Core.Config;

public static class ConfigLoader
{
    public static CarbonConfig Load(string? path = null)
    {
        if (path is null)
        {
            using var bundled = Host.EmbeddedAssetStore.OpenConfig();
            if (bundled is not null)
                return Deserialize(bundled);

            path = FindConfigFile();
        }

        if (path is not null && File.Exists(path))
        {
            using var file = File.OpenRead(path);
            var config = Deserialize(file);
            LoadExternalCapabilities(config, Path.GetDirectoryName(Path.GetFullPath(path))!);
            return config;
        }

        Console.WriteLine("[Carbon] No carbon.json found, using defaults");
        return new CarbonConfig();
    }

    private static CarbonConfig Deserialize(Stream json) =>
        JsonSerializer.Deserialize(json, CarbonConfigJsonContext.Default.CarbonConfig)
            ?? new CarbonConfig();

    public static void Save(CarbonConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        JsonSerializer.Serialize(file, config, CarbonConfigJsonContext.Default.CarbonConfig);
    }

    private static void LoadExternalCapabilities(CarbonConfig config, string projectDir)
    {
        foreach (var capabilityDir in CapabilityDirectories(projectDir))
        {
            if (!Directory.Exists(capabilityDir)) continue;

            foreach (var file in Directory.GetFiles(capabilityDir, "*.json"))
            {
                using var stream = File.OpenRead(file);
                var capability = JsonSerializer.Deserialize(
                    stream,
                    CarbonConfigJsonContext.Default.CapabilityConfig);
                if (capability is null) continue;

                var id = string.IsNullOrWhiteSpace(capability.Identifier)
                    ? Path.GetFileNameWithoutExtension(file)
                    : capability.Identifier;
                capability.Identifier = id;
                config.Security.Capabilities[id] = capability;
            }
        }
    }

    private static IEnumerable<string> CapabilityDirectories(string projectDir)
    {
        yield return Path.Combine(projectDir, "src-carbon", "capabilities");
        yield return Path.Combine(projectDir, "capabilities");
    }

    private static string? FindConfigFile()
    {
        // Dev: walk up from the working directory (carbon dev runs at the project root).
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "carbon.json");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        // Compatibility with older builds that shipped carbon.json beside the executable.
        var beside = Path.Combine(AppContext.BaseDirectory, "carbon.json");
        return File.Exists(beside) ? beside : null;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CarbonConfig))]
[JsonSerializable(typeof(CapabilityConfig))]
[JsonSerializable(typeof(RemoteConfig))]
internal partial class CarbonConfigJsonContext : JsonSerializerContext;
