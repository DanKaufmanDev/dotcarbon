using System.Reflection;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// The C# side of a Carbon project — the host (<c>src-carbon</c>) and the shared command library
/// (<c>src-shared</c>). Shared by <c>carbon init</c> (adopt into a frontend) and <c>carbon migrate</c>
/// (import from Tauri): both add Carbon to a project that already has a frontend, so both need the
/// exact same C# scaffold. The layout is not cosmetic — the mobile generators reference
/// <c>..\..\..\src-shared\AppLogic.csproj</c> and the <c>AppState</c>/<c>AppCommands</c> types by name,
/// so <c>carbon platform add</c> only works if the scaffold lands here.
/// </summary>
internal static class ProjectScaffold
{
    public const string BackendDir = "src-carbon";
    public const string SharedDir = "src-shared";

    /// <summary>The C# files to write for an app named <paramref name="appName"/>, rooted at <paramref name="root"/>.</summary>
    public static IReadOnlyList<(string Path, string Content)> CSharpFiles(string root, string appName, string version) =>
    [
        (Path.Combine(root, BackendDir, $"{appName}.csproj"), HostProject(appName, version)),
        (Path.Combine(root, BackendDir, "Program.cs"), ProgramCs()),
        (Path.Combine(root, BackendDir, "capabilities", "main.json"), MainCapability()),
        (Path.Combine(root, SharedDir, "AppLogic.csproj"), SharedProject(version)),
        (Path.Combine(root, SharedDir, "AppCommands.cs"), AppCommands()),
    ];

    private static string HostProject(string appName, string version) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <OutputType>Exe</OutputType>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        $"    <AssemblyName>{appName}</AssemblyName>\n" +
        "  </PropertyGroup>\n\n" +
        "  <ItemGroup>\n" +
        $"    <PackageReference Include=\"DotCarbon.Host.Desktop\" Version=\"{version}\" />\n" +
        $"    <ProjectReference Include=\"..\\{SharedDir}\\AppLogic.csproj\" />\n" +
        "  </ItemGroup>\n\n" +
        "</Project>\n";

    private static string ProgramCs() =>
        "using DotCarbon.Core.Config;\n" +
        "using DotCarbon.Core.Runtime;\n" +
        "using DotCarbon.Host.Desktop;\n\n" +
        "var config = ConfigLoader.Load();\n\n" +
        "CarbonApp.Create(config)\n" +
        "    .UseDesktop()\n" +
        "    .Manage(new AppState())\n" +
        "    .UsePlugin<AppCommands>()\n" +
        "    .Run();\n";

    private static string MainCapability() =>
        "{\n" +
        "    \"description\": \"Main window permissions.\",\n" +
        "    \"windows\": [\"main\"],\n" +
        "    \"commands\": [\"app:greet\", \"window:*\", \"core:event_emit\"]\n" +
        "}\n";

    private static string SharedProject(string version) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "    <ImplicitUsings>enable</ImplicitUsings>\n" +
        "  </PropertyGroup>\n\n" +
        "  <ItemGroup>\n" +
        $"    <PackageReference Include=\"DotCarbon.Core\" Version=\"{version}\" />\n" +
        "  </ItemGroup>\n\n" +
        "</Project>\n";

    private static string AppCommands() =>
        "using DotCarbon.Core.Bridge;\n" +
        "using DotCarbon.Core.Plugins;\n\n" +
        "// Desktop and any generated mobile hosts share this command assembly.\n\n" +
        "public record GreetRequest(string Name);\n\n" +
        "public sealed class AppState\n" +
        "{\n" +
        "    public int GreetingCount { get; set; }\n" +
        "}\n\n" +
        "public partial class AppCommands : IPlugin\n" +
        "{\n" +
        "    private readonly AppState _state;\n\n" +
        "    public AppCommands(AppState state)\n" +
        "    {\n" +
        "        _state = state;\n" +
        "    }\n\n" +
        "    public string Namespace => \"app\";\n\n" +
        "    [CarbonCommand(\"greet\")]\n" +
        "    public string Greet(GreetRequest req)\n" +
        "    {\n" +
        "        _state.GreetingCount++;\n" +
        "        return $\"Hello, {req.Name}! You've been greeted {_state.GreetingCount} time(s) from C# ⚡\";\n" +
        "    }\n" +
        "}\n";

    /// <summary>Scaffolded package references track the CLI, so the toolchain and the app stay in step.</summary>
    public static string CarbonVersion()
    {
        var informational = typeof(ProjectScaffold).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "*" : version;
    }

    /// <summary>An assembly name has to be an identifier, and app names (npm, productName) often are not.</summary>
    public static string SanitizeAppName(string name)
    {
        var scoped = name.StartsWith('@') && name.Contains('/') ? name[(name.IndexOf('/') + 1)..] : name;
        var cleaned = new string(scoped.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0) return "App";
        return char.IsDigit(cleaned[0]) ? $"App{cleaned}" : char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }
}
