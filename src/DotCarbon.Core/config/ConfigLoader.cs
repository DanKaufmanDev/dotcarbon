using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCarbon.Core.Config;

public static class ConfigLoader
{
    public static CarbonConfig Load(string? path = null)
    {
        path ??= FindConfigFile();

        if (path is null || !File.Exists(path))
        {
            Console.WriteLine("No carbon.json found, using defaults");
            return new CarbonConfig();
        }

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize(json, CarbonConfigJsonContext.Default.CarbonConfig)
            ?? new CarbonConfig();
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

        // Prod: a distributed app runs with cwd=/ — carbon.json ships next to the exe.
        var beside = Path.Combine(AppContext.BaseDirectory, "carbon.json");
        return File.Exists(beside) ? beside : null;
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CarbonConfig))]
internal partial class CarbonConfigJsonContext : JsonSerializerContext;
