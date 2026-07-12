using DotCarbon.Core.Config;

namespace DotCarbon.Tests;

internal static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static CarbonConfig Load(string name) => ConfigLoader.Load(Path.Combine(Dir, name));

    /// <summary>Create a throwaway project dir with a src-carbon/App.csproj containing the given XML.</summary>
    public static string TempProject(string csproj)
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "src-carbon"));
        File.WriteAllText(Path.Combine(dir, "src-carbon", "App.csproj"), csproj);
        return dir;
    }

    public static CarbonConfig App(string name = "Test", string version = "1.0.0", string id = "com.example.test") =>
        new() { App = new AppConfig { Name = name, Version = version, Identifier = id } };
}
