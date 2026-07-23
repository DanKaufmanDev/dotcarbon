using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.3: `carbon info` prints a paste-able block of exact versions for bug reports. The probe is
/// injected, so these tests assert against a known toolchain rather than whatever is installed on the
/// machine running them.
/// </summary>
public class InfoCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carbon-info-{Guid.NewGuid():N}");

    public InfoCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Reports_the_toolchain_versions_a_bug_report_needs()
    {
        var report = Render(new FakeProbe());

        Assert.Contains("macOS 15.3 (arm64)", report);
        Assert.Contains("10.0.301", report);
        Assert.Contains("android 36.1.69, ios 26.5.10284", report);
        Assert.Contains("v24.13.0", report);
        Assert.Contains("WKWebView (system)", report);
    }

    [Fact]
    public void Missing_tools_are_reported_as_missing_rather_than_omitted()
    {
        // "not found" is the useful answer in a bug report; a blank line is not.
        var probe = new FakeProbe();
        probe.Outputs.Remove("node");
        probe.Outputs.Remove("pnpm");

        var report = Render(probe);

        Assert.Matches(@"Node\s+not found", report);
        Assert.Matches(@"pnpm\s+not found", report);
    }

    [Fact]
    public void Workload_manifest_versions_are_parsed_out_of_the_dotnet_table()
    {
        var probe = new FakeProbe();
        probe.Outputs["dotnet workload list"] =
            "Installed Workload Id      Manifest Version      Installation Source\n" +
            "--------------------------------------------------------------------\n" +
            "android                    36.1.69/10.0.100      SDK 10.0.100\n" +
            "ios                        26.5.10284/10.0.100   SDK 10.0.100\n";

        Assert.Contains("android 36.1.69, ios 26.5.10284", Render(probe));
    }

    [Fact]
    public void No_workloads_installed_is_stated_explicitly()
    {
        // Distinguishing "none installed" from "could not ask" matters when diagnosing mobile builds.
        var probe = new FakeProbe();
        probe.Outputs["dotnet workload list"] =
            "Installed Workload Id      Manifest Version      Installation Source\n" +
            "--------------------------------------------------------------------\n";

        Assert.Matches(@"Workloads\s+none installed", Render(probe));
    }

    [Fact]
    public void Linux_reports_the_webkitgtk_version_it_finds()
    {
        var probe = new FakeProbe { Platform = "linux" };
        probe.Outputs["pkg-config --modversion webkit2gtk-4.1"] = "2.46.5";

        Assert.Contains("webkit2gtk-4.1 2.46.5", Render(probe));
    }

    [Fact]
    public void Outside_a_project_the_app_section_says_so_instead_of_inventing_values()
    {
        var report = Render(new FakeProbe());

        Assert.Contains("no carbon.json here", report);
    }

    [Fact]
    public void Inside_a_project_it_reports_the_app_config_and_package_versions()
    {
        WriteProject();

        var report = Render(new FakeProbe());

        Assert.Contains("Demo (com.example.demo) 1.2.3", report);
        Assert.Contains("http://localhost:5173", report);
        Assert.Contains("DotCarbon.Host.Desktop", report);
        Assert.Contains("0.7.0 (src-carbon/Demo.csproj)", report);
        Assert.Contains("DotCarbon.Core", report);
        Assert.Contains("@dotcarbon/api", report);
    }

    [Fact]
    public void Package_versions_come_from_the_host_project_graph_not_the_whole_tree()
    {
        // A template carrying an unsubstituted placeholder, and a fixture pinned to an ancient
        // package, must not be reported as this app's versions.
        WriteProject();
        Directory.CreateDirectory(Path.Combine(_root, "templates", "src-carbon"));
        File.WriteAllText(Path.Combine(_root, "templates", "src-carbon", "Template.csproj"),
            Csproj("DotCarbon.Host.Desktop", "{{DOTCARBON_VERSION}}"));
        Directory.CreateDirectory(Path.Combine(_root, "fixtures"));
        File.WriteAllText(Path.Combine(_root, "fixtures", "Old.csproj"), Csproj("DotCarbon.Core", "0.1.0"));

        var report = Render(new FakeProbe());

        Assert.DoesNotContain("{{DOTCARBON_VERSION}}", report);
        Assert.DoesNotContain("0.1.0 (fixtures", report);
        Assert.Contains("0.7.0", report);
    }

    [Fact]
    public void Third_party_plugins_are_listed_from_their_permission_manifests()
    {
        // PluginCompatibility only matches DotCarbon.Plugins.* package references, so without this a
        // project-referenced or third-party plugin shows up as "none referenced".
        WriteProject();
        Directory.CreateDirectory(Path.Combine(_root, "src-carbon", "permissions"));
        File.WriteAllText(Path.Combine(_root, "src-carbon", "permissions", "confetti.json"),
            """
            {
                "namespace": "confetti",
                "permissions": [{ "identifier": "confetti:default", "commands": ["confetti:burst"] }]
            }
            """);

        Assert.Matches(@"Plugins\s+confetti", Render(new FakeProbe()));
    }

    [Fact]
    public void A_broken_config_is_reported_rather_than_crashing_the_report()
    {
        File.WriteAllText(Path.Combine(_root, "carbon.json"), "{ this is not json");

        var report = Render(new FakeProbe());

        Assert.Contains("failed to load", report);
        Assert.Contains("Environment", report);
    }

    private string Render(FakeProbe probe) => InfoCommand.Render(InfoCommand.Collect(_root, probe));

    private void WriteProject()
    {
        File.WriteAllText(Path.Combine(_root, "carbon.json"),
            """
            {
                "app": { "name": "Demo", "version": "1.2.3", "identifier": "com.example.demo" },
                "build": {
                    "devUrl": "http://localhost:5173",
                    "frontendDist": "dist",
                    "backendProject": "src-carbon"
                }
            }
            """);
        File.WriteAllText(Path.Combine(_root, "package.json"),
            """{ "name": "demo", "dependencies": { "@dotcarbon/api": "^0.7.0" } }""");

        Directory.CreateDirectory(Path.Combine(_root, "src-carbon"));
        File.WriteAllText(Path.Combine(_root, "src-carbon", "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"DotCarbon.Host.Desktop\" Version=\"0.7.0\" />\n" +
            "    <ProjectReference Include=\"..\\src-shared\\AppLogic.csproj\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        Directory.CreateDirectory(Path.Combine(_root, "src-shared"));
        File.WriteAllText(Path.Combine(_root, "src-shared", "AppLogic.csproj"), Csproj("DotCarbon.Core", "0.7.0"));
    }

    private static string Csproj(string package, string version) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n" +
        $"  <ItemGroup><PackageReference Include=\"{package}\" Version=\"{version}\" /></ItemGroup>\n" +
        "</Project>\n";

    /// <summary>A known toolchain: every probe answers from this table, or reports as missing.</summary>
    private sealed class FakeProbe : IEnvironmentProbe
    {
        public Dictionary<string, string> Outputs { get; } = new(StringComparer.Ordinal)
        {
            ["sw_vers -productVersion"] = "15.3",
            ["dotnet --version"] = "10.0.301",
            ["dotnet --list-runtimes"] =
                "Microsoft.AspNetCore.App 10.0.9 [/usr/share]\nMicrosoft.NETCore.App 10.0.9 [/usr/share]",
            ["dotnet workload list"] =
                "Installed Workload Id      Manifest Version      Installation Source\n" +
                "--------------------------------------------------------------------\n" +
                "android                    36.1.69/10.0.100      SDK 10.0.100\n" +
                "ios                        26.5.10284/10.0.100   SDK 10.0.100\n",
            ["node"] = "v24.13.0",
            ["npm"] = "11.9.0",
            ["pnpm"] = "10.0.0",
            ["git"] = "git version 2.47.1",
            ["xcodebuild"] = "Xcode 26.5",
        };

        public string Platform { get; init; } = "macos";

        public string Architecture => "arm64";

        public string? Run(string fileName, params string[] arguments)
        {
            var key = arguments.Length == 0 ? fileName : $"{fileName} {string.Join(' ', arguments)}";
            if (Outputs.TryGetValue(key, out var full)) return full;
            // Version probes are keyed by tool name alone for brevity.
            return arguments is ["--version"] or ["-version"] && Outputs.TryGetValue(fileName, out var version)
                ? version
                : null;
        }

        public string? GetEnvironmentVariable(string name) => null;

        public bool DirectoryExists(string path) => false;
    }
}
