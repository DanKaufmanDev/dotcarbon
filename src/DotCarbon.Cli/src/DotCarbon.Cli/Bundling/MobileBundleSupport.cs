using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        string platformDir,
        string project,
        string frontendDist,
        string configPath,
        string propsName,
        string? baseOutputPath = null)
    {
        var generatedDir = Path.Combine(platformDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var propsPath = Path.Combine(generatedDir, propsName);
        // MSBuild resolves symlinked project paths before setting MSBuildProjectFullPath. Match on
        // the project file name so a Carbon app opened through a symlink still gets its resources
        // and project-scoped output path, while referenced backend projects remain untouched.
        var projectFile = Path.GetFileName(project).Replace("'", "%27");
        var condition = $"'$(MSBuildProjectFile)' == '{projectFile}'";
        var projectElement = new XElement("Project");
        if (!string.IsNullOrWhiteSpace(baseOutputPath))
        {
            projectElement.Add(
                new XElement("PropertyGroup",
                    new XAttribute("Condition", condition),
                    new XElement("BaseOutputPath", EnsureTrailingSeparator(baseOutputPath))));
        }

        projectElement.Add(
            new XElement("ItemGroup",
                new XAttribute("Condition", condition),
                new XElement("EmbeddedResource",
                    new XAttribute("Include", Path.Combine(frontendDist, "**", "*")),
                    new XAttribute("LogicalName", "DotCarbon.Assets/%(RecursiveDir)%(Filename)%(Extension)")),
                new XElement("EmbeddedResource",
                    new XAttribute("Include", configPath),
                    new XAttribute("LogicalName", "DotCarbon.Config/carbon.json"))));

        var document = new XDocument(projectElement);
        document.Save(propsPath);
        return propsPath;
    }

    /// <summary>
    /// Writes targets imported after Microsoft.Common.targets. A props import is too early for an
    /// iOS <c>BeforeTargets</c> hook to reliably join the final codesign graph.
    /// </summary>
    public static string WriteIosCodesignTargets(string platformDir)
    {
        var generatedDir = Path.Combine(platformDir, "obj", "dotcarbon");
        Directory.CreateDirectory(generatedDir);
        var targetsPath = Path.Combine(generatedDir, "DotCarbon.iOS.targets");
        var document = new XDocument(
            new XElement("Project",
                new XElement("Target",
                    new XAttribute("Name", "CarbonStripExtendedAttributesBeforeCodesign"),
                    new XAttribute("BeforeTargets", "_CodesignAppBundle"),
                    new XAttribute("Condition",
                        "'$(OS)' == 'Unix' And Exists('/usr/bin/xattr') And Exists('$(AppBundleDir)')"),
                    new XElement("Exec",
                        new XAttribute("Command", "/usr/bin/xattr -cr \"$(AppBundleDir)\"")))));
        document.Save(targetsPath);
        return targetsPath;
    }

    /// <summary>
    /// iOS app bundles are built outside cloud-backed source folders so Finder/file-provider
    /// attributes cannot be reattached between cleanup and codesign.
    /// </summary>
    public static string LocalBuildRoot(string workingDir, string platform)
    {
        var normalized = Path.GetFullPath(workingDir).TrimEnd(Path.DirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..12];
        return Path.Combine(Path.GetTempPath(), "dotcarbon", "build", hash, platform);
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    /// <summary>
    /// Finds a usable JDK, including the JetBrains Runtime bundled with Android Studio. The .NET
    /// Android workload does not discover that runtime on every macOS installation.
    /// </summary>
    internal static string? FindJavaSdkDirectory(IEnumerable<string?>? candidates = null)
    {
        candidates ??= JavaSdkCandidates();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var path = Environment.ExpandEnvironmentVariables(candidate);
            var java = Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(java)) return Path.GetFullPath(path);
        }

        return null;
    }

    private static IEnumerable<string?> JavaSdkCandidates()
    {
        yield return Environment.GetEnvironmentVariable("JAVA_HOME");
        yield return Environment.GetEnvironmentVariable("ANDROID_JAVA_HOME");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Android Studio.app/Contents/jbr/Contents/Home";
            yield return "/Applications/Android Studio Preview.app/Contents/jbr/Contents/Home";
            yield return Path.Combine(home, "Applications", "Android Studio.app", "Contents", "jbr", "Contents", "Home");
        }
        else if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Android", "Android Studio", "jbr");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Android Studio", "jbr");
        }
        else
        {
            yield return "/opt/android-studio/jbr";
            yield return "/usr/local/android-studio/jbr";
            yield return Path.Combine(home, "android-studio", "jbr");
        }
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
    /// codesign stays clean. The injected iOS targets repeat this immediately before codesign so
    /// cloud file providers cannot reattach attributes between preparation and signing. No-op off
    /// macOS or if <c>xattr</c> is unavailable.
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
