using System.Diagnostics;
using System.Xml.Linq;

namespace DotCarbon.Cli.Bundling;

/// <summary>Shared helpers for the mobile (Android/iOS) bundlers: asset embedding and workload checks.</summary>
internal static class MobileBundleSupport
{
    /// <summary>
    /// Writes an MSBuild props file that embeds the built frontend and carbon.json into the app
    /// assembly (as the same manifest resources EmbeddedAssetStore reads), scoped to one project.
    /// </summary>
    public static string WriteEmbedProps(
        string platformDir, string project, string frontendDist, string configPath, string propsName)
    {
        var generatedDir = Path.Combine(platformDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var propsPath = Path.Combine(generatedDir, propsName);
        var condition = $"'$(MSBuildProjectFullPath)' == '{project.Replace("'", "%27")}'";
        var document = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    new XAttribute("Condition", condition),
                    new XElement("EmbeddedResource",
                        new XAttribute("Include", Path.Combine(frontendDist, "**", "*")),
                        new XAttribute("LogicalName", "DotCarbon.Assets/%(RecursiveDir)%(Filename)%(Extension)")),
                    new XElement("EmbeddedResource",
                        new XAttribute("Include", configPath),
                        new XAttribute("LogicalName", "DotCarbon.Config/carbon.json")))));
        document.Save(propsPath);
        return propsPath;
    }

    /// <summary>True if <c>dotnet workload list</c> reports the given workload id installed.</summary>
    public static async Task<bool> HasWorkload(string workload)
    {
        try
        {
            var info = new ProcessStartInfo("dotnet", "workload list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(info);
            if (process is null) return false;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Contains(workload, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fails the bundle if the app references plugins that don't support the target platform,
    /// unless <paramref name="allowUnsupported"/> (then it only warns). Returns true to proceed.
    /// </summary>
    public static bool EnsurePluginsCompatible(string workingDir, string platform, bool allowUnsupported)
    {
        var incompatible = PluginCompatibility.Incompatible(workingDir, platform);
        if (incompatible.Count == 0) return true;

        var names = string.Join(", ", incompatible.Select(plugin => plugin.Namespace));
        if (allowUnsupported)
        {
            Warn($"Bundling {platform} with {incompatible.Count} unsupported plugin(s): {names}");
            return true;
        }

        Error($"These plugins do not support {platform}: {names}.");
        Error($"Remove them for {platform} or pass --allow-unsupported-plugins to bundle anyway. See `carbon doctor`.");
        return false;
    }

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    private static void Write(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
