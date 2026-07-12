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
                new("Build frontend", "vite build → embedded into the iOS app assembly"),
                new(mode == "archive" ? "Publish archive" : "Build .NET iOS",
                    $"dotnet {(mode == "archive" ? "publish" : "build")} -f net10.0-ios -c {configuration} -p:RuntimeIdentifier={rid}"),
                new("Locate artifact", mode == "archive" ? "the .ipa under bin/…/publish" : $"the .app under bin/{configuration}"),
            },
        };
    }

    public async Task<int> ExecuteAsync(CarbonConfig config, string workingDir, string mode, bool dryRun)
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

        var props = await PrepareAsync(config, workingDir, iosDir, project);
        if (props is null) return 1;

        var (configuration, rid, publish, archive) = ResolveMode(mode);
        Console.WriteLine("\n[Carbon] Step 2/2 — Building .NET iOS app...");
        var verb = publish ? "publish" : "build";
        var args =
            $"{verb} \"{project}\" -c {configuration} -f net10.0-ios -p:RuntimeIdentifier={rid} " +
            (archive ? "-p:ArchiveOnBuild=true " : string.Empty) +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        if (await BuildCommand.RunProcessToCompletion("dotnet", args, iosDir, "[ios]", ConsoleColor.Magenta) != 0)
        {
            MobileBundleSupport.Error(".NET iOS build failed.");
            return 1;
        }

        var pattern = archive ? "*.ipa" : "*.app";
        var artifact = Directory
            .EnumerateFileSystemEntries(iosDir, pattern, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(artifact is not null
            ? $"\n[Carbon] Build complete -> {Path.GetRelativePath(workingDir, artifact)}"
            : $"\n[Carbon] Build finished; look under {Path.GetRelativePath(workingDir, iosDir)}/bin/{configuration}.");
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

        var props = await PrepareAsync(config, workingDir, iosDir, project);
        if (props is null) return 1;

        var args =
            $"build \"{project}\" -c Debug -f net10.0-ios -t:Run -p:RuntimeIdentifier={SimulatorRid()} " +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
        return await BuildCommand.RunProcessToCompletion("dotnet", args, iosDir, "[ios]", ConsoleColor.Magenta);
    }

    private static string? FindProject(string iosDir) =>
        Directory.Exists(iosDir) ? Directory.GetFiles(iosDir, "*.csproj").FirstOrDefault() : null;

    private static async Task<string?> PrepareAsync(
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

        return MobileBundleSupport.WriteEmbedProps(
            iosDir, project, frontendDist, Path.Combine(workingDir, "carbon.json"), "DotCarbon.iOS.props");
    }

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
