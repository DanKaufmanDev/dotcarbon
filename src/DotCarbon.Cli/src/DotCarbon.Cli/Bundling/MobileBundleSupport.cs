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

    /// <summary>
    /// macOS <c>codesign</c> rejects app bundles whose files carry extended attributes ("resource
    /// fork, Finder information, or similar detritus not allowed") — these accumulate across
    /// incremental dev builds. Best-effort strip of the platform output dir so the build's internal
    /// codesign stays clean. No-op off macOS or if <c>xattr</c> is unavailable.
    /// </summary>
    public static void StripExtendedAttributes(string dir)
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists(dir)) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("xattr", $"-cr \"{dir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(10_000);
        }
        catch
        {
            // Best-effort: if it fails, the build will still surface any codesign issue itself.
        }
    }

    /// <summary>
    /// Extra guidance to print when an iOS build fails: the most common non-obvious cause on a dev
    /// machine is codesign choking on file-provider "detritus" because the project sits in a
    /// cloud-synced folder (iCloud/OneDrive/Dropbox). Phrased conditionally so it only helps.
    /// </summary>
    public static void HintIosBuildFailure()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Warn("If it failed at codesign ('resource fork … detritus not allowed'), the project is in a");
        Warn("cloud-synced folder (iCloud/OneDrive/Dropbox) — build from a non-synced path (e.g. ~/dev/…).");
    }

    /// <summary>
    /// Warns (with a concrete fix) when the active Xcode's major.minor differs from the installed
    /// .NET iOS workload's — otherwise the mismatch only shows up as a cryptic MSBuild
    /// "This version of .NET for iOS requires Xcode X" error deep in the build log.
    /// </summary>
    public static async Task WarnIfXcodeMismatchAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var xcode = MajorMinor(FirstLineValue(await CaptureAsync("xcodebuild", "-version"), "Xcode "));
        var workload = MajorMinor(IosWorkloadToken(await CaptureAsync("dotnet", "workload list")));
        if (xcode is null || workload is null || xcode == workload) return;

        Warn($"Xcode {xcode} does not match the installed .NET iOS workload (built for Xcode {workload}).");
        Warn("The iOS build will likely fail with a 'requires Xcode' error. Align the two, then retry:");
        Warn("  • update the workload:      sudo dotnet workload update   (if an Xcode-matching one exists), or");
        Warn("  • select the matching Xcode: sudo xcode-select -s /Applications/<matching Xcode>.app");
    }

    private static async Task<string> CaptureAsync(string fileName, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return string.Empty;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    // "Xcode 26.6\nBuild version ..." → "26.6"
    private static string? FirstLineValue(string text, string prefix) =>
        text.Split('\n')
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?[prefix.Length..].Trim();

    // a `dotnet workload list` row like "ios   26.5.10284/10.0.100   SDK 10.0.300" → "26.5.10284/10.0.100"
    private static string? IosWorkloadToken(string workloadList) =>
        workloadList.Split('\n')
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(cols => cols.Length >= 2 && cols[0].Equals("ios", StringComparison.OrdinalIgnoreCase))
            ?[1];

    private static string? MajorMinor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var parts = version.Split('.', '/', '-');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
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
