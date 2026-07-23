using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon info</c> — one paste-able block of exact versions for bug reports: toolchain, Carbon
/// packages and the app's own configuration. Reports what it finds and says "not found" where it
/// finds nothing, rather than guessing.
/// </summary>
public static class InfoCommand
{
    internal sealed record InfoSection(string Title, IReadOnlyList<InfoEntry> Entries);

    internal sealed record InfoEntry(string Label, string? Value);

    public static Command Build()
    {
        var command = new Command("info", "Print environment and project versions for bug reports");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        command.AddOption(project);

        command.SetHandler(context =>
        {
            var workingDir = context.ParseResult.GetValueForOption(project)?.FullName
                ?? Directory.GetCurrentDirectory();
            Console.WriteLine(Render(Collect(workingDir, new ProcessProbe())));
        });

        return command;
    }

    internal static IReadOnlyList<InfoSection> Collect(string workingDir, IEnvironmentProbe probe)
    {
        var configPath = Path.Combine(workingDir, "carbon.json");
        CarbonConfig? config = null;
        if (File.Exists(configPath))
        {
            try { config = ConfigLoader.Load(configPath); }
            catch { config = null; }
        }

        return
        [
            new InfoSection("Environment", Environment(probe)),
            new InfoSection("Carbon", Carbon(workingDir, config)),
            File.Exists(configPath)
                ? new InfoSection("App", App(config, workingDir))
                : new InfoSection("App", [new InfoEntry("carbon.json", null)]),
        ];
    }

    private static IReadOnlyList<InfoEntry> Environment(IEnvironmentProbe probe)
    {
        var entries = new List<InfoEntry>
        {
            new("OS", OperatingSystemName(probe)),
            new(".NET SDK", FirstLine(probe.Run("dotnet", "--version"))),
            new(".NET runtimes", Runtimes(probe)),
            new("Workloads", Workloads(probe)),
            new("Webview", Webview(probe)),
            new("Node", FirstLine(probe.Run("node", "--version"))),
            new("npm", FirstLine(probe.Run("npm", "--version"))),
            new("pnpm", FirstLine(probe.Run("pnpm", "--version"))),
            new("yarn", FirstLine(probe.Run("yarn", "--version"))),
            new("bun", FirstLine(probe.Run("bun", "--version"))),
        };

        if (probe.Platform == "macos")
            entries.Add(new InfoEntry("Xcode", FirstLine(probe.Run("xcodebuild", "-version"))));

        entries.Add(new InfoEntry("Android SDK", AndroidSdk(probe)));
        entries.Add(new InfoEntry("JDK", Jdk(probe)));
        entries.Add(new InfoEntry("git", FirstLine(probe.Run("git", "--version"))));
        return entries;
    }

    private static string? OperatingSystemName(IEnvironmentProbe probe)
    {
        var version = probe.Platform switch
        {
            "macos" => FirstLine(probe.Run("sw_vers", "-productVersion")),
            "linux" => FirstLine(probe.Run("uname", "-r")),
            _ => System.Environment.OSVersion.Version.ToString(),
        };

        var name = probe.Platform switch
        {
            "macos" => "macOS",
            "windows" => "Windows",
            _ => "Linux",
        };

        return $"{name} {version ?? "unknown"} ({probe.Architecture})";
    }

    private static string? Runtimes(IEnvironmentProbe probe)
    {
        var output = probe.Run("dotnet", "--list-runtimes");
        if (output is null) return null;

        // Lines look like "Microsoft.NETCore.App 10.0.9 [/path]"; the app runtime is the one that matters.
        var versions = output.Split('\n')
            .Where(line => line.StartsWith("Microsoft.NETCore.App ", StringComparison.Ordinal))
            .Select(line => line.Split(' ') is { Length: > 1 } parts ? parts[1] : null)
            .Where(version => version is not null)
            .Distinct()
            .ToList();

        return versions.Count == 0 ? null : string.Join(", ", versions);
    }

    /// <summary>Mobile builds fail confusingly when the android/ios workloads are missing or skewed.</summary>
    private static string? Workloads(IEnvironmentProbe probe)
    {
        var output = probe.Run("dotnet", "workload", "list");
        if (output is null) return null;

        var workloads = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (parts[0] is "Installed" or "---" || parts[0].StartsWith("---", StringComparison.Ordinal)) continue;
            // "android  36.1.69/10.0.100  SDK 10.0.100" — the manifest version is the useful half.
            if (!parts[1].Contains('.')) continue;
            workloads.Add($"{parts[0]} {parts[1].Split('/')[0]}");
        }

        return workloads.Count == 0 ? "none installed" : string.Join(", ", workloads);
    }

    /// <summary>The webview is the runtime dependency users hit first, and it differs per platform.</summary>
    private static string? Webview(IEnvironmentProbe probe) => probe.Platform switch
    {
        "macos" => "WKWebView (system)",
        "linux" => probe.Run("pkg-config", "--modversion", "webkit2gtk-4.1") is { } modern
            ? $"webkit2gtk-4.1 {FirstLine(modern)}"
            : probe.Run("pkg-config", "--modversion", "webkit2gtk-4.0") is { } legacy
                ? $"webkit2gtk-4.0 {FirstLine(legacy)}"
                : null,
        _ => WebView2(probe),
    };

    private static string? WebView2(IEnvironmentProbe probe)
    {
        // The Evergreen runtime records its version under the EdgeUpdate client key.
        const string key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" +
                           "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        var output = probe.Run("reg", "query", key, "/v", "pv");
        if (output is null) return null;

        var parts = output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(parts, part => part.Equals("REG_SZ", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < parts.Length ? $"WebView2 {parts[index + 1]}" : null;
    }

    private static string? AndroidSdk(IEnvironmentProbe probe)
    {
        var home = new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" }
            .Select(probe.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        home ??= new[]
        {
            Path.Combine(probe.GetEnvironmentVariable("HOME") ?? string.Empty, "Library", "Android", "sdk"),
            Path.Combine(probe.GetEnvironmentVariable("HOME") ?? string.Empty, "Android", "Sdk"),
            Path.Combine(probe.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty, "Android", "Sdk"),
        }.FirstOrDefault(probe.DirectoryExists);

        if (string.IsNullOrWhiteSpace(home) || !probe.DirectoryExists(home)) return null;

        var adb = FirstLine(probe.Run(Path.Combine(home, "platform-tools", "adb"), "--version"));
        return adb is null ? home : $"{home} ({adb})";
    }

    private static string? Jdk(IEnvironmentProbe probe)
    {
        var javaHome = probe.GetEnvironmentVariable("JAVA_HOME");
        var version = FirstLine(probe.Run(
            string.IsNullOrWhiteSpace(javaHome) ? "java" : Path.Combine(javaHome, "bin", "java"), "-version"));

        // Android builds need a JDK the .NET Android SDK can find; Android Studio bundles one when
        // there is no system java, and that is the common local setup on macOS.
        if (version is null && probe.Platform == "macos")
        {
            const string bundled = "/Applications/Android Studio.app/Contents/jbr/Contents/Home";
            if (probe.DirectoryExists(bundled))
                return FirstLine(probe.Run(Path.Combine(bundled, "bin", "java"), "-version")) is { } bundledVersion
                    ? $"{bundledVersion} (Android Studio; set JAVA_HOME to use it)"
                    : $"{bundled} (Android Studio)";
        }

        if (version is null) return null;
        return string.IsNullOrWhiteSpace(javaHome) ? version : $"{version} (JAVA_HOME={javaHome})";
    }

    private static IReadOnlyList<InfoEntry> Carbon(string workingDir, CarbonConfig? config)
    {
        var entries = new List<InfoEntry> { new("CLI", CliVersion()) };

        foreach (var (package, version, project) in ReferencedPackages(workingDir, config))
            entries.Add(new InfoEntry(package, $"{version} ({project})"));

        foreach (var (package, version) in JsPackages(workingDir, config))
            entries.Add(new InfoEntry(package, version));

        return entries;
    }

    /// <summary>
    /// DotCarbon.* package references. Scoped to the host project and the projects it references, so
    /// unrelated csproj files elsewhere in the tree — templates carrying placeholder versions, sample
    /// apps, test fixtures pinned to an old package — cannot be reported as the app's versions.
    /// </summary>
    private static IEnumerable<(string Package, string Version, string Project)> ReferencedPackages(
        string workingDir, CarbonConfig? config)
    {
        var results = new SortedDictionary<string, (string Version, string Project)>(StringComparer.Ordinal);

        var host = config is null ? null : ProjectLocator.FindHostProject(workingDir, config);
        var projects = host is null ? EnumerateFiles(workingDir, "*.csproj") : ProjectGraph(host);

        foreach (var path in projects)
        {
            XDocument document;
            try { document = XDocument.Load(path); }
            catch { continue; }

            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "PackageReference"))
            {
                var package = (string?)reference.Attribute("Include");
                var version = (string?)reference.Attribute("Version");
                if (package is null || version is null) continue;
                if (!package.StartsWith("DotCarbon.", StringComparison.OrdinalIgnoreCase)) continue;
                // Unsubstituted template placeholders are not versions.
                if (version.Contains("{{", StringComparison.Ordinal)) continue;

                // Several projects in the graph may reference the same package (a plugin outside the
                // app directory, for instance). Attribute it to the app's own project when possible.
                var relative = Path.GetRelativePath(workingDir, path);
                if (!results.TryGetValue(package, out var existing) || Closer(relative, existing.Project))
                    results[package] = (version, relative);
            }
        }

        return results.Select(entry => (entry.Key, entry.Value.Version, entry.Value.Project));
    }

    /// <summary>
    /// The host project plus every project it references, transitively.
    /// </summary>
    private static IEnumerable<string> ProjectGraph(string host)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([host]);

        while (pending.Count > 0)
        {
            var path = pending.Pop();
            if (!File.Exists(path) || !seen.Add(path)) continue;
            yield return path;

            XDocument document;
            try { document = XDocument.Load(path); }
            catch { continue; }

            var directory = Path.GetDirectoryName(path)!;
            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "ProjectReference"))
            {
                if ((string?)reference.Attribute("Include") is not { } include) continue;
                pending.Push(Path.GetFullPath(Path.Combine(directory, include.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }
    }

    /// <summary>
    /// @dotcarbon/* dependencies. Scoped to the frontend package.json when the config points at one
    /// (same derivation `carbon dev` uses), so template and example packages are not reported.
    /// </summary>
    private static IEnumerable<(string Package, string Version)> JsPackages(string workingDir, CarbonConfig? config)
    {
        var results = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in FrontendPackageJson(workingDir, config) is { } frontend
                     ? [frontend]
                     : EnumerateFiles(workingDir, "package.json"))
        {
            JsonNode? root;
            try { root = JsonNode.Parse(File.ReadAllText(path)); }
            catch { continue; }

            foreach (var section in new[] { "dependencies", "devDependencies" })
            {
                if (root?[section] is not JsonObject dependencies) continue;
                foreach (var entry in dependencies)
                {
                    if (!entry.Key.StartsWith("@dotcarbon/", StringComparison.OrdinalIgnoreCase)) continue;
                    results.TryAdd(entry.Key, entry.Value?.ToString() ?? "?");
                }
            }
        }

        return results.Select(entry => (entry.Key, entry.Value));
    }

    /// <summary>Paths inside the project beat paths outside it; shallower beats deeper.</summary>
    private static bool Closer(string candidate, string current)
    {
        var candidateOutside = candidate.StartsWith("..", StringComparison.Ordinal);
        var currentOutside = current.StartsWith("..", StringComparison.Ordinal);
        if (candidateOutside != currentOutside) return currentOutside;
        return candidate.Length < current.Length;
    }

    private static string? FrontendPackageJson(string workingDir, CarbonConfig? config)
    {
        if (config is null) return null;

        var directory = Path.GetFullPath(Path.Combine(
            workingDir, Path.GetDirectoryName(config.Build.FrontendDist) ?? string.Empty));

        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "package.json");
            if (File.Exists(candidate)) return candidate;
            if (current.FullName == workingDir) break;
        }

        var root = Path.Combine(workingDir, "package.json");
        return File.Exists(root) ? root : null;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(root, path)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part is "bin" or "obj" or "node_modules" or "out" or "dist"));
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<InfoEntry> App(CarbonConfig? config, string workingDir)
    {
        if (config is null) return [new InfoEntry("carbon.json", "present, but failed to load")];

        var platforms = PlatformService.KnownIds
            .Where(id => Directory.Exists(PlatformService.PlatformDir(workingDir, id)))
            .ToList();
        // First-party plugin packages, plus any third-party plugin that ships a permission manifest —
        // PluginCompatibility only matches DotCarbon.Plugins.* PackageReferences, so on its own it
        // reports "none referenced" for a third-party or project-referenced plugin.
        var plugins = PluginCompatibility.Discover(workingDir).Select(plugin => plugin.Namespace)
            .Concat(CapabilityPermissionCatalog.Discover(workingDir).Select(permission => permission.PluginNamespace))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return
        [
            new("Name", $"{config.App.Name} ({config.App.Identifier}) {config.App.Version}"),
            new("Targets", config.Bundle.Targets.Count > 0 ? string.Join(", ", config.Bundle.Targets) : "desktop"),
            new("devUrl", config.Build.DevUrl),
            new("frontendDist", config.Build.FrontendDist),
            new("backendProject", config.Build.BackendProject),
            new("Security", config.Security.Enabled ? "enabled" : "disabled"),
            new("Plugins", plugins.Count > 0 ? string.Join(", ", plugins) : "none referenced"),
            new("Platforms added", platforms.Count > 0 ? string.Join(", ", platforms) : "none"),
        ];
    }

    /// <summary>Renders the block users paste into an issue: fixed-width labels, "not found" for gaps.</summary>
    internal static string Render(IReadOnlyList<InfoSection> sections)
    {
        var builder = new StringBuilder();
        builder.AppendLine("⚡ Carbon info");

        foreach (var section in sections)
        {
            builder.AppendLine();
            builder.AppendLine(section.Title);

            if (section.Title == "App" && section.Entries is [{ Label: "carbon.json", Value: null }])
            {
                builder.AppendLine("  no carbon.json here — run `carbon info` inside a Carbon project for app details");
                continue;
            }

            var width = section.Entries.Max(entry => entry.Label.Length);
            foreach (var entry in section.Entries)
                builder.AppendLine($"  {entry.Label.PadRight(width)}  {entry.Value ?? "not found"}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string CliVersion()
    {
        var informational = typeof(InfoCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? "unknown";
    }

    private static string? FirstLine(string? value) =>
        value?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
}
