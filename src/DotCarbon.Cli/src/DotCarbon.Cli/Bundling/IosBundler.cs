using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
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

        // Report toolchain drift before MSBuild fails deep in the native targets.
        await MobileBundleSupport.WarnIfXcodeMismatchAsync();

        var prepared = await PrepareAsync(config, workingDir, iosDir, project);
        if (prepared is null) return 1;

        // File providers can attach metadata that codesign rejects.
        MobileBundleSupport.StripExtendedAttributes(prepared.ProjectDirectory);
        Environment.SetEnvironmentVariable("COPYFILE_DISABLE", "1");

        var (configuration, rid, publish, archive) = ResolveMode(mode);

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
            $"{verb} \"{prepared.Project}\" -c {configuration} -f net10.0-ios -p:RuntimeIdentifier={rid} " +
            (archive ? "-p:ArchiveOnBuild=true " : string.Empty) +
            (string.IsNullOrEmpty(signingArgs) ? string.Empty : signingArgs + " ") +
            $"-p:CustomBeforeMicrosoftCommonProps=\"{prepared.EmbedProps}\" " +
            $"-p:CustomAfterMicrosoftCommonTargets=\"{prepared.CodesignTargets}\"";
        if (await BuildCommand.RunProcessToCompletion(
                "dotnet", args, prepared.ProjectDirectory, "[ios]", ConsoleColor.Magenta) != 0)
        {
            MobileBundleSupport.Error(".NET iOS build failed.");
            MobileBundleSupport.HintIosBuildFailure();
            return 1;
        }

        var artifact = LocateArtifact(prepared.ProjectDirectory, archive, configuration);
        if (artifact is not null && Directory.Exists(artifact) && !HasBundleExecutable(artifact))
        {
            MobileBundleSupport.Error(
                $"The generated app bundle is incomplete: no executable was found in {artifact}.");
            MobileBundleSupport.Error(
                "Check the .NET iOS build output and active Xcode/workload versions, then retry.");
            return 1;
        }
        if (artifact is not null)
            artifact = PublishArtifact(artifact, workingDir, mode);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(artifact is not null
            ? $"\n[Carbon] Build complete -> {Path.GetRelativePath(workingDir, artifact)}"
            : $"\n[Carbon] Build finished, but the signed iOS artifact could not be located.");
        Console.ResetColor();
        return 0;
    }

    /// <summary>
    /// Runs the app on a booted iOS simulator in development mode: the frontend is served live by the
    /// dev server (hot reload) rather than embedded, and the app's stdout is streamed to the terminal.
    /// The simulator shares the host's loopback, so the on-device webview reaches the host dev server
    /// directly at <c>build.devUrl</c> (e.g. http://localhost:5173).
    /// </summary>
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
        Console.WriteLine("⚡ Carbon ios dev — hot-reload build on the booted simulator...");
        Console.ResetColor();
        Console.WriteLine("  (boot a simulator first, e.g. `xcrun simctl boot \"iPhone 17\"` or open Simulator.app)");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine("\n[Carbon] Shutting down ios dev...");
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            // 1. Bring up the frontend dev server (or reuse one already running) so the on-device
            //    webview has something to load over http://localhost.
            var devUrl = config.Build.DevUrl;
            Task? devServer = null;
            if (await MobileBundleSupport.IsReachable(devUrl, cts.Token))
            {
                Console.WriteLine($"[Carbon] Reusing dev server already running at {devUrl}");
            }
            else
            {
                devServer = MobileBundleSupport.StartDevServer(config, workingDir, cts.Token);
                Console.WriteLine($"[Carbon] Waiting for dev server at {devUrl}...");
                if (!await MobileBundleSupport.WaitForDevServer(devUrl, TimeSpan.FromSeconds(60), cts.Token))
                {
                    MobileBundleSupport.Error(
                        $"Frontend dev server never became reachable at {devUrl}. " +
                        "Check build.devCommand and build.devUrl in carbon.json.");
                    return 1;
                }
            }
            _ = devServer; // kept alive by the shared CancellationTokenSource until we shut down

            await MobileBundleSupport.WarnIfXcodeMismatchAsync();

            // 2. Build a dev app bundle that embeds only carbon.json (no frontend) so the host picks
            //    DevServer content mode, with an ATS exception allowing the plaintext-http dev server.
            var prepared = PrepareDev(config, workingDir, iosDir, project);
            MobileBundleSupport.StripExtendedAttributes(prepared.ProjectDirectory);
            Environment.SetEnvironmentVariable("COPYFILE_DISABLE", "1");

            Console.WriteLine("\n[Carbon] Building .NET iOS app (dev)...");
            // Simulator dev builds run under the Mono interpreter: fast rebuilds and no device-only
            // AOT step (which the iOS SDK's AOT compiler can crash on, and which a dev loop never needs).
            var buildArgs =
                $"build \"{prepared.Project}\" -c Debug -f net10.0-ios -p:RuntimeIdentifier={SimulatorRid()} " +
                "-p:MtouchInterpreter=all -p:RunAOTCompilation=false " +
                $"-p:CustomBeforeMicrosoftCommonProps=\"{prepared.EmbedProps}\" " +
                $"-p:CustomAfterMicrosoftCommonTargets=\"{prepared.CodesignTargets}\"";
            if (await BuildCommand.RunProcessToCompletion(
                    "dotnet", buildArgs, prepared.ProjectDirectory, "[ios]", ConsoleColor.Magenta) != 0)
            {
                MobileBundleSupport.Error(".NET iOS build failed.");
                MobileBundleSupport.HintIosBuildFailure();
                return 1;
            }

            // 3. Install onto the booted simulator and launch with a pty so the app's stdout streams
            //    back here (Console.WriteLine, including CarbonApp's content-mode banner).
            var appBundle = LocateArtifact(prepared.ProjectDirectory, archive: false, "Debug");
            if (appBundle is null)
            {
                MobileBundleSupport.Error("Build finished but the .app bundle could not be located.");
                return 1;
            }

            Console.WriteLine($"[Carbon] Installing {Path.GetFileName(appBundle)} on the booted simulator...");
            if (await MobileBundleSupport.RunStreaming("xcrun", $"simctl install booted \"{appBundle}\"",
                    prepared.ProjectDirectory, "[sim]", ConsoleColor.Blue, cts.Token) != 0)
            {
                MobileBundleSupport.Error(
                    "simctl install failed. Boot a simulator first: xcrun simctl boot \"iPhone 17\".");
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Carbon] Launching {config.App.Identifier} — streaming logs (Ctrl-C to stop)...");
            Console.ResetColor();
            await MobileBundleSupport.RunStreaming(
                "xcrun",
                $"simctl launch --console-pty --terminate-running-process booted {config.App.Identifier}",
                prepared.ProjectDirectory, "[app]", ConsoleColor.Magenta, cts.Token);

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            cts.Cancel();
        }
    }

    /// <summary>
    /// Prepares a dev build: stages the project, embeds only carbon.json (no frontend, so the host
    /// runs in DevServer mode), and relaxes ATS so the webview can load the http dev server.
    /// </summary>
    private static PreparedBuild PrepareDev(
        CarbonConfig config, string workingDir, string iosDir, string project)
    {
        var buildRoot = MobileBundleSupport.LocalBuildRoot(workingDir, "ios");
        var stagedProject = StageProject(iosDir, project, buildRoot);
        var stagedDir = Path.GetDirectoryName(stagedProject)!;
        var props = MobileBundleSupport.WriteDevConfigProps(
            stagedDir, stagedProject, Path.Combine(workingDir, "carbon.json"), "DotCarbon.iOS.props");
        MobileBundleSupport.InjectLocalNetworkingAts(stagedDir);
        var targets = MobileBundleSupport.WriteIosCodesignTargets(stagedDir);
        return new PreparedBuild(stagedProject, stagedDir, props, targets);
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

    internal static bool HasBundleExecutable(string appBundle)
    {
        if (!Directory.Exists(appBundle) || OperatingSystem.IsWindows()) return false;

        foreach (var file in Directory.EnumerateFiles(appBundle, "*", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetUnixFileMode(file) & UnixFileMode.UserExecute) != 0) return true;
        }

        return false;
    }

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
        var stagedProject = StageProject(iosDir, project, buildRoot);
        var stagedDir = Path.GetDirectoryName(stagedProject)!;
        var props = MobileBundleSupport.WriteEmbedProps(
            stagedDir,
            stagedProject,
            frontendDist,
            Path.Combine(workingDir, "carbon.json"),
            "DotCarbon.iOS.props");
        var targets = MobileBundleSupport.WriteIosCodesignTargets(stagedDir);
        return new PreparedBuild(stagedProject, stagedDir, props, targets);
    }

    /// <summary>
    /// Copies the generated iOS project into Carbon's local build cache. Building the project from
    /// there keeps the iOS SDK's native-link and app-bundle paths internally consistent while
    /// avoiding file-provider attributes on cloud-backed source folders.
    /// </summary>
    internal static string StageProject(string iosDir, string project, string buildRoot)
    {
        var stagedDir = Path.Combine(buildRoot, "project");
        if (Directory.Exists(stagedDir)) Directory.Delete(stagedDir, recursive: true);
        CopyProjectSources(iosDir, stagedDir);

        var stagedProject = Path.Combine(stagedDir, Path.GetFileName(project));
        var document = XDocument.Load(stagedProject, LoadOptions.PreserveWhitespace);
        foreach (var reference in document.Descendants().Where(node => node.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attribute("Include");
            if (include is null || Path.IsPathRooted(include.Value)) continue;
            var relative = include.Value
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            include.Value = ResolvePhysicalPath(Path.Combine(iosDir, relative));
        }
        document.Save(stagedProject, SaveOptions.DisableFormatting);
        return stagedProject;
    }

    private static string ResolvePhysicalPath(string path) =>
        MobileBundleSupport.ResolvePhysicalPath(path);

    private static void CopyProjectSources(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj") continue;
            CopyProjectSources(directory, Path.Combine(destination, name));
        }
    }

    internal static string PublishArtifact(string artifact, string workingDir, string mode)
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
            CopyFile(artifact, destination);
        }

        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyFile(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }

    private sealed record PreparedBuild(
        string Project, string ProjectDirectory, string EmbedProps, string CodesignTargets);

    // Keep mode selection in one place so planning and execution stay aligned.
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
