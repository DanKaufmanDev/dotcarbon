using System.Xml.Linq;
using DotCarbon.Cli.Commands;

namespace DotCarbon.Cli.Bundling;

/// <summary>A first-party plugin the app references, and the platforms it supports.</summary>
internal sealed record ReferencedPlugin(string Package, string Namespace, IReadOnlyList<string> Platforms)
{
    public bool Supports(string platform) =>
        Platforms.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Discovers which DotCarbon plugins an app references (from its csproj files) and answers whether
/// they support a given platform. Used by <c>carbon doctor</c> and the mobile bundler compat gate.
/// </summary>
internal static class PluginCompatibility
{
    public static readonly string[] Platforms = ["desktop", "android", "ios"];

    /// <summary>Map a bundle target (osx-arm64, win-x64, android, ios, …) to a platform id.</summary>
    public static string PlatformForTarget(string target) =>
        target.StartsWith("android", StringComparison.OrdinalIgnoreCase) ? "android" :
        target.StartsWith("ios", StringComparison.OrdinalIgnoreCase) ? "ios" :
        "desktop";

    public static IReadOnlyList<ReferencedPlugin> Discover(string workingDir)
    {
        var byPackage = new Dictionary<string, ReferencedPlugin>(StringComparer.OrdinalIgnoreCase);

        foreach (var csproj in EnumerateProjects(workingDir))
        {
            foreach (var package in ReferencedPackages(csproj))
            {
                if (!package.StartsWith("DotCarbon.Plugins.", StringComparison.OrdinalIgnoreCase)) continue;
                if (byPackage.ContainsKey(package)) continue;

                var definition = AddCommand.Catalog.Values
                    .FirstOrDefault(d => d.NuGetPackage.Equals(package, StringComparison.OrdinalIgnoreCase));
                byPackage[package] = definition is not null
                    ? new ReferencedPlugin(package, definition.Namespace, definition.EffectivePlatforms)
                    : new ReferencedPlugin(package, package, Platforms); // unknown plugin → assume cross-platform
            }
        }

        return byPackage.Values.OrderBy(p => p.Namespace, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<ReferencedPlugin> Incompatible(string workingDir, string platform) =>
        Discover(workingDir).Where(plugin => !plugin.Supports(platform)).ToList();

    private static IEnumerable<string> EnumerateProjects(string workingDir) =>
        Directory.EnumerateFiles(workingDir, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnored(workingDir, path));

    private static bool IsIgnored(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or "out" or "node_modules" or ".carbon");
    }

    private static IEnumerable<string> ReferencedPackages(string csproj)
    {
        XDocument document;
        try { document = XDocument.Load(csproj); }
        catch { yield break; }

        foreach (var element in document.Descendants())
        {
            var kind = element.Name.LocalName;
            if (kind is not ("PackageReference" or "ProjectReference")) continue;
            var include = (string?)element.Attribute("Include");
            if (include is null) continue;

            if (kind == "PackageReference")
            {
                // A NuGet package id (e.g. DotCarbon.Plugins.FileSystem) — use it verbatim.
                yield return include;
            }
            else
            {
                // A .csproj path — the project name is the file name without extension.
                var fileName = include.Replace('\\', '/').Split('/').Last();
                yield return Path.GetFileNameWithoutExtension(fileName);
            }
        }
    }
}
