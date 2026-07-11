using System.Xml.Linq;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

internal static class ProjectLocator
{
    public static string? FindHostProject(string workingDir, CarbonConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Build.BackendProject))
            return ResolveConfiguredProject(workingDir, config.Build.BackendProject);

        var projects = Directory.EnumerateFiles(workingDir, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !HasIgnoredDirectory(workingDir, path))
            .Select(path => (Path: path, Project: TryLoad(path)))
            .Where(item => item.Project is not null && IsExecutable(item.Project))
            .ToList();

        var desktopHosts = projects
            .Where(item =>
                References(item.Project!, "DotCarbon.Host.Desktop") ||
                References(item.Project!, "Photino.NET") ||
                References(item.Project!, "DotCarbon.Host"))
            .Select(item => item.Path)
            .ToList();
        if (desktopHosts.Count == 1) return desktopHosts[0];

        var carbonHosts = projects
            .Where(item => References(item.Project!, "DotCarbon.Core"))
            .Select(item => item.Path)
            .ToList();
        return carbonHosts.Count == 1 ? carbonHosts[0] : null;
    }

    private static string? ResolveConfiguredProject(string workingDir, string configured)
    {
        var path = Path.GetFullPath(Path.Combine(workingDir, configured));
        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return path;
        if (!Directory.Exists(path)) return null;

        var projects = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
        return projects.Length == 1 ? projects[0] : null;
    }

    private static XDocument? TryLoad(string path)
    {
        try { return XDocument.Load(path); }
        catch { return null; }
    }

    private static bool IsExecutable(XDocument project) => project.Descendants()
        .Any(element => element.Name.LocalName == "OutputType" &&
            (element.Value.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
             element.Value.Equals("WinExe", StringComparison.OrdinalIgnoreCase)));

    private static bool References(XDocument project, string name) => project.Descendants()
        .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
        .Select(element => (string?)element.Attribute("Include"))
        .Any(value =>
        {
            if (value is null) return false;
            var fileName = value.Replace('\\', '/').Split('/').Last();
            return value.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                   Path.GetFileNameWithoutExtension(fileName).Equals(name, StringComparison.OrdinalIgnoreCase);
        });

    private static bool HasIgnoredDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or "node_modules" or "out");
    }
}
