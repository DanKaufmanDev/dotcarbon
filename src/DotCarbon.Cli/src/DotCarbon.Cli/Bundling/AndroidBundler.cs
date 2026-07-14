using DotCarbon.Cli.Commands;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Packages a Carbon app for Android by publishing the generated .NET Android project
/// (<c>.carbon/platforms/android</c>) to an APK or AAB. Requires the Android workload.
/// </summary>
internal sealed class AndroidBundler
{
    public BundlePlan Plan(CarbonConfig config, string format, bool release)
    {
        var configuration = release ? "Release" : "Debug";
        return new BundlePlan
        {
            TargetId = "android",
            TargetName = $"Android ({configuration}, .{format})",
            Summary = $"one Carbon app → .{format} via .NET Android",
            Steps = new List<BundleStep>
            {
                new("Validate", "android platform added (`carbon platform add android`) + Android workload"),
                new("Build frontend", "build command or existing dist → embedded into the Android app assembly"),
                new("Publish .NET Android", $"dotnet publish -f net10.0-android -c {configuration} (AndroidPackageFormat={format})"),
                new("Locate artifact", $"the .{format} under the project's bin/{configuration} output"),
            },
        };
    }

    public async Task<int> ExecuteAsync(
        CarbonConfig config, string workingDir, string format, bool release, bool dryRun, bool allowUnsupported)
    {
        var androidDir = PlatformService.PlatformDir(workingDir, "android");
        var project = FindProject(androidDir);
        if (project is null)
        {
            MobileBundleSupport.Error("No Android project found. Run `carbon platform add android` first.");
            return 1;
        }

        if (dryRun)
        {
            Plan(config, format, release).Render(dryRun: true);
            return 0;
        }

        Plan(config, format, release).Render(dryRun: false);

        if (!MobileBundleSupport.EnsurePluginsCompatible(workingDir, "android", allowUnsupported)) return 1;

        if (PlatformService.NeedsSync(config, workingDir, "android"))
            MobileBundleSupport.Warn("Android project is out of sync with carbon.json — run `carbon platform sync android` to apply config/permission changes.");

        if (!SigningSupport.TryAndroidSigningArgs(config, workingDir, release, out var signingArgs, out var signingError))
        {
            MobileBundleSupport.Error(signingError);
            return 1;
        }

        if (!await MobileBundleSupport.HasWorkload("android"))
        {
            MobileBundleSupport.Error("The .NET Android workload is not installed. Run: dotnet workload install android");
            return 1;
        }

        var props = await PrepareAsync(config, workingDir, androidDir, project);
        if (props is null) return 1;

        Console.WriteLine("\n[Carbon] Step 2/2 — Publishing .NET Android app...");
        var configuration = release ? "Release" : "Debug";
        var args =
            $"publish \"{project}\" -c {configuration} -f net10.0-android " +
            $"-p:AndroidPackageFormat={format} " +
            // A bundled APK/AAB is installed standalone, so assemblies must be embedded. In Debug the
            // default is Fast Deployment (assemblies deployed separately by `dotnet run`), which makes
            // an adb-installed APK abort at startup with "No assemblies found". Force embedding here;
            // it is already the default for Release.
            "-p:EmbedAssembliesIntoApk=true " +
            (string.IsNullOrEmpty(signingArgs) ? string.Empty : signingArgs + " ") +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        if (await BuildCommand.RunProcessToCompletion("dotnet", args, androidDir, "[android]", ConsoleColor.Magenta) != 0)
        {
            MobileBundleSupport.Error(".NET Android publish failed.");
            return 1;
        }

        var artifact = LocateArtifact(androidDir, format, configuration);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(artifact is not null
            ? $"\n[Carbon] Build complete -> {Path.GetRelativePath(workingDir, artifact)}"
            : $"\n[Carbon] Publish finished; look for the .{format} under {Path.GetRelativePath(workingDir, androidDir)}/bin/{configuration}.");
        Console.ResetColor();
        return 0;
    }

    /// <summary>
    /// Debug build + deploy/run on a connected device or emulator (`dotnet build -t:Run`).
    /// A hot-reload dev loop (Vite over the emulator's 10.0.2.2 host bridge) is roadmap Phase 11.
    /// </summary>
    public async Task<int> DevAsync(CarbonConfig config, string workingDir)
    {
        var androidDir = PlatformService.PlatformDir(workingDir, "android");
        var project = FindProject(androidDir);
        if (project is null)
        {
            MobileBundleSupport.Error("No Android project found. Run `carbon platform add android` first.");
            return 1;
        }
        if (!await MobileBundleSupport.HasWorkload("android"))
        {
            MobileBundleSupport.Error("The .NET Android workload is not installed. Run: dotnet workload install android");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚡ Carbon android dev — building and deploying to a device/emulator...");
        Console.ResetColor();
        Console.WriteLine("  (needs a running emulator or a connected device via adb)");

        var props = await PrepareAsync(config, workingDir, androidDir, project);
        if (props is null) return 1;

        var args =
            $"build \"{project}\" -c Debug -f net10.0-android -t:Run " +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        return await BuildCommand.RunProcessToCompletion("dotnet", args, androidDir, "[android]", ConsoleColor.Magenta);
    }

    private static string? FindProject(string androidDir) =>
        Directory.Exists(androidDir) ? Directory.GetFiles(androidDir, "*.csproj").FirstOrDefault() : null;

    internal static string? LocateArtifact(string androidDir, string format, string configuration)
    {
        var binDir = Path.Combine(androidDir, "bin", configuration);
        var candidates = Directory.Exists(binDir)
            ? Directory.EnumerateFiles(binDir, $"*.{format}", SearchOption.AllDirectories).ToList()
            : [];

        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(path => IsPreferredApk(path, format))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsPreferredApk(string path, string format) =>
        format.Equals("apk", StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileNameWithoutExtension(path).EndsWith("-Signed", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> PrepareAsync(
        CarbonConfig config, string workingDir, string androidDir, string project)
    {
        Console.WriteLine("\n[Carbon] Step 1/2 — Building frontend...");
        if (!await BuildCommand.BuildFrontend(config, workingDir))
        {
            MobileBundleSupport.Error("Frontend build failed. Aborting.");
            return null;
        }

        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        if (!File.Exists(Path.Combine(frontendDist, "index.html")))
        {
            MobileBundleSupport.Error($"Frontend output does not contain index.html: {frontendDist}");
            return null;
        }

        return MobileBundleSupport.WriteEmbedProps(
            androidDir, project, frontendDist, Path.Combine(workingDir, "carbon.json"), "DotCarbon.Android.props");
    }
}
