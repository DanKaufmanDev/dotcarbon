using System.Runtime.InteropServices;
using DotCarbon.Cli.Commands;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Bundling;

/// <summary>
/// Packages a Carbon app for iOS by building the generated .NET iOS project
/// (<c>.carbon/platforms/ios</c>) for the simulator, a device, or an archive (.ipa).
/// Requires macOS + Xcode + the iOS workload.
/// </summary>
internal sealed class IosBundler
{
    public BundlePlan Plan(CarbonConfig config, string mode)
    {
        var (configuration, rid, _, _) = ResolveMode(mode);
        return new BundlePlan
        {
            TargetId = "ios",
            TargetName = $"iOS ({mode})",
            Summary = mode == "archive"
                ? "one Carbon app → signed .ipa archive"
                : $"one Carbon app → {mode} build ({rid})",
            Steps = new List<BundleStep>
            {
                new("Validate", "macOS + Xcode + iOS workload, and the ios platform added"),
                new("Build frontend", "build command or existing dist → embedded into the iOS app assembly"),
                new(mode == "archive" ? "Publish archive" : "Build .NET iOS",
                    $"dotnet {(mode == "archive" ? "publish" : "build")} -f net10.0-ios -c {configuration} -p:RuntimeIdentifier={rid}"),
                new("Locate artifact", $"the signed artifact under out/ios/{mode}"),
            },
        };
    }

    public async Task<int> ExecuteAsync(
        CarbonConfig config, string workingDir, string mode, bool dryRun, bool allowUnsupported)
    {
        var iosDir = PlatformService.PlatformDir(workingDir, "ios");
        var project = FindProject(iosDir);
        if (project is null)
        {
            MobileBundleSupport.Error("No iOS project found. Run `carbon platform add ios` first.");
            return 1;
        }

        if (dryRun)
        {
            Plan(config, mode).Render(dryRun: true);
            return 0;
        }

        Plan(config, mode).Render(dryRun: false);

        if (!MobileBundleSupport.EnsurePluginsCompatible(workingDir, "ios", allowUnsupported)) return 1;

        if (PlatformService.NeedsSync(config, workingDir, "ios"))
            MobileBundleSupport.Warn("iOS project is out of sync with carbon.json — run `carbon platform sync ios` to apply config/permission changes.");

        if (!OperatingSystem.IsMacOS())
        {
            MobileBundleSupport.Error("iOS apps can only be built on macOS with Xcode installed.");
            return 1;
        }
        if (!await MobileBundleSupport.HasWorkload("ios"))
        {
            MobileBundleSupport.Error("The .NET iOS workload is not installed. Run: dotnet workload install ios");
            return 1;
        }

        // Surface the Xcode/workload mismatch up front with a fix, instead of a cryptic MSBuild error.
        await MobileBundleSupport.WarnIfXcodeMismatchAsync();

        var prepared = await PrepareAsync(config, workingDir, iosDir, project);
        if (prepared is null) return 1;

        // Best-effort strip of accumulated extended attributes so a repeat dev build's codesign
        // doesn't choke on "detritus" (won't beat an actively-syncing cloud folder — see the warning).
        MobileBundleSupport.StripExtendedAttributes(iosDir);
        Environment.SetEnvironmentVariable("COPYFILE_DISABLE", "1");

        var (configuration, rid, publish, archive) = ResolveMode(mode);

        // Device/archive builds must be signed.
        var signingArgs = string.Empty;
        if (mode is "device" or "archive")
        {
            if (!SigningSupport.IosCanSign(config))
                MobileBundleSupport.Warn(
                    $"No signing identity configured — {mode} builds need bundle.ios.signing.identity " +
                    "(and usually provisioningProfile). See `carbon doctor signing`.");
            signingArgs = SigningSupport.IosSigningArgs(config);
        }

        Console.WriteLine("\n[Carbon] Step 2/2 — Building .NET iOS app...");
        var verb = publish ? "publish" : "build";
        var args =
            $"{verb} \"{project}\" -c {configuration} -f net10.0-ios -p:RuntimeIdentifier={rid} " +
            (archive ? "-p:ArchiveOnBuild=true " : string.Empty) +
            (string.IsNullOrEmpty(signingArgs) ? string.Empty : signingArgs + " ") +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{prepared.EmbedProps}\" " +
            $"-p:CustomAfterMicrosoftCommonTargets=\"{prepared.CodesignTargets}\"";
        if (await BuildCommand.RunProcessToCompletion("dotnet", args, iosDir, "[ios]", ConsoleColor.Magenta) != 0)
        {
            MobileBundleSupport.Error(".NET iOS build failed.");
            MobileBundleSupport.HintIosBuildFailure();
            return 1;
        }

        var artifact = LocateArtifact(prepared.BuildRoot, archive, configuration);
        if (artifact is not null)
            artifact = PublishArtifact(artifact, workingDir, mode);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(artifact is not null
            ? $"\n[Carbon] Build complete -> {Path.GetRelativePath(workingDir, artifact)}"
            : $"\n[Carbon] Build finished, but the signed iOS artifact could not be located.");
        Console.ResetColor();
        return 0;
    }

    /// <summary>Build + run on a booted simulator (`dotnet build -t:Run`). Hot reload is roadmap Phase 11.</summary>
    public async Task<int> DevAsync(CarbonConfig config, string workingDir)
    {
        var iosDir = PlatformService.PlatformDir(workingDir, "ios");
        var project = FindProject(iosDir);
        if (project is null)
        {
            MobileBundleSupport.Error("No iOS project found. Run `carbon platform add ios` first.");
            return 1;
        }
        if (!OperatingSystem.IsMacOS())
        {
            MobileBundleSupport.Error("iOS apps can only be built on macOS with Xcode installed.");
            return 1;
        }
        if (!await MobileBundleSupport.HasWorkload("ios"))
        {
            MobileBundleSupport.Error("The .NET iOS workload is not installed. Run: dotnet workload install ios");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("⚡ Carbon ios dev — building and running on the booted simulator...");
        Console.ResetColor();
        Console.WriteLine("  (boot a simulator first, e.g. `xcrun simctl boot \"iPhone 15\"` or open Simulator.app)");

        await MobileBundleSupport.WarnIfXcodeMismatchAsync();

        var prepared = await PrepareAsync(config, workingDir, iosDir, project);
        if (prepared is null) return 1;

        MobileBundleSupport.StripExtendedAttributes(iosDir);
        Environment.SetEnvironmentVariable("COPYFILE_DISABLE", "1");

        var args =
            $"build \"{project}\" -c Debug -f net10.0-ios -t:Run -p:RuntimeIdentifier={SimulatorRid()} " +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{prepared.EmbedProps}\" " +
            $"-p:CustomAfterMicrosoftCommonTargets=\"{prepared.CodesignTargets}\"";
        return await BuildCommand.RunProcessToCompletion("dotnet", args, iosDir, "[ios]", ConsoleColor.Magenta);
    }

    private static string? FindProject(string iosDir) =>
        Directory.Exists(iosDir) ? Directory.GetFiles(iosDir, "*.csproj").FirstOrDefault() : null;

    internal static string? LocateArtifact(string iosDir, bool archive, string configuration)
    {
        var binDir = Path.Combine(iosDir, "bin", configuration);
        if (!Directory.Exists(binDir)) return null;

        var pattern = archive ? "*.ipa" : "*.app";
        return Directory
            .EnumerateFileSystemEntries(binDir, pattern, SearchOption.AllDirectories)
            .OrderByDescending(LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static DateTime LastWriteTimeUtc(string path) =>
        Directory.Exists(path)
            ? Directory.GetLastWriteTimeUtc(path)
            : File.GetLastWriteTimeUtc(path);

    private static async Task<PreparedBuild?> PrepareAsync(
        CarbonConfig config, string workingDir, string iosDir, string project)
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

        var buildRoot = MobileBundleSupport.LocalBuildRoot(workingDir, "ios");
        var props = MobileBundleSupport.WriteEmbedProps(
            iosDir,
            project,
            frontendDist,
            Path.Combine(workingDir, "carbon.json"),
            "DotCarbon.iOS.props",
            baseOutputPath: Path.Combine(buildRoot, "bin"));
        var targets = MobileBundleSupport.WriteIosCodesignTargets(iosDir);
        return new PreparedBuild(props, targets, buildRoot);
    }

    private static string PublishArtifact(string artifact, string workingDir, string mode)
    {
        var destinationDir = Path.Combine(workingDir, "out", "ios", mode);
        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, Path.GetFileName(artifact));

        if (Directory.Exists(artifact))
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            CopyDirectory(artifact, destination);
        }
        else
        {
            File.Copy(artifact, destination, overwrite: true);
        }

        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed record PreparedBuild(string EmbedProps, string CodesignTargets, string BuildRoot);

    // mode → (configuration, runtimeIdentifier, usePublish, archive)
    private static (string Configuration, string Rid, bool Publish, bool Archive) ResolveMode(string mode) => mode switch
    {
        "device" => ("Release", "ios-arm64", false, false),
        "archive" => ("Release", "ios-arm64", true, true),
        _ => ("Debug", SimulatorRid(), false, false), // simulator
    };

    private static string SimulatorRid() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "iossimulator-arm64"
            : "iossimulator-x64";
}
