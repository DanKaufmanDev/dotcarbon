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
                new("Validate", "android platform added (`carbon platform add android`) + Android workload + JDK"),
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

        var javaSdk = MobileBundleSupport.FindJavaSdkDirectory();
        if (javaSdk is not null)
            Console.WriteLine($"[Carbon] Android JDK -> {javaSdk}");

        var props = await PrepareAsync(config, workingDir, androidDir, project);
        if (props is null) return 1;

        Console.WriteLine("\n[Carbon] Step 2/2 — Publishing .NET Android app...");
        var configuration = release ? "Release" : "Debug";
        var args =
            $"publish \"{project}\" -c {configuration} -f net10.0-android " +
            $"-p:AndroidPackageFormat={format} " +
            // Debug defaults to fast deployment, which leaves assemblies outside the APK. Bundles
            // must be self-contained because users and CI install them directly.
            "-p:EmbedAssembliesIntoApk=true " +
            (javaSdk is null ? string.Empty : $"-p:JavaSdkDirectory=\"{javaSdk}\" ") +
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
    /// Runs the app on a device/emulator in development mode: the frontend is served live by the dev
    /// server (hot reload) rather than embedded, and the app's logcat is streamed to the terminal.
    /// Unlike the iOS simulator, an Android device does not share the host loopback, so
    /// <c>adb reverse</c> maps the device's <c>localhost:&lt;port&gt;</c> back to the host dev server —
    /// which keeps the same <c>build.devUrl</c> working on both platforms.
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
        Console.WriteLine("⚡ Carbon android dev — hot-reload build on a device/emulator...");
        Console.ResetColor();
        Console.WriteLine("  (needs a running emulator or a connected device — check with `adb devices`)");

        var adb = MobileBundleSupport.FindAdb();
        var javaSdk = MobileBundleSupport.FindJavaSdkDirectory();
        if (javaSdk is not null)
            Console.WriteLine($"[Carbon] Android JDK -> {javaSdk}");

        var devUrl = config.Build.DevUrl;
        var port = new Uri(devUrl).Port;

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine("\n[Carbon] Shutting down android dev...");
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            // 1. Bring up (or reuse) the frontend dev server.
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

            // 2. Forward the device's localhost:<port> to the host dev server (hot reload on device).
            Console.WriteLine($"[Carbon] adb reverse tcp:{port} -> host tcp:{port}");
            if (await MobileBundleSupport.RunStreaming(
                    adb, $"reverse tcp:{port} tcp:{port}", androidDir, "[adb]", ConsoleColor.Blue, cts.Token) != 0)
            {
                MobileBundleSupport.Error(
                    "adb reverse failed — is an emulator running or a device connected? Check `adb devices`.");
                return 1;
            }

            // 3. Dev build: embed only carbon.json (no frontend, so the host uses DevServer mode) plus a
            //    manifest overlay allowing cleartext http so the WebView can reach the local dev server.
            var props = MobileBundleSupport.WriteAndroidDevProps(
                androidDir, project, Path.Combine(workingDir, "carbon.json"), "DotCarbon.Android.props");

            Console.WriteLine("\n[Carbon] Building .NET Android app (dev) and deploying...");
            var buildArgs =
                $"build \"{project}\" -c Debug -f net10.0-android -t:Run " +
                (javaSdk is null ? string.Empty : $"-p:JavaSdkDirectory=\"{javaSdk}\" ") +
                $"-p:CustomBeforeMicrosoftCommonProps=\"{props}\"";
            if (await BuildCommand.RunProcessToCompletion(
                    "dotnet", buildArgs, androidDir, "[android]", ConsoleColor.Magenta) != 0)
            {
                MobileBundleSupport.Error(".NET Android build/deploy failed.");
                return 1;
            }

            // 4. Stream the app's logcat until Ctrl-C. logcat replays the ring buffer first, so the
            //    startup lines (content-mode banner, bridge round-trip) are captured even post-launch.
            var package = AndroidPackage(config);
            var pid = await WaitForAppPid(adb, package, cts.Token);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Carbon] Streaming logcat for {package} (Ctrl-C to stop)...");
            Console.ResetColor();
            var logArgs = pid is null ? "logcat -v brief" : $"logcat -v brief --pid={pid}";
            await MobileBundleSupport.RunStreaming(adb, logArgs, androidDir, "[app]", ConsoleColor.Magenta, cts.Token);

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            cts.Cancel();
            await MobileBundleSupport.RunCapture(adb, $"reverse --remove tcp:{port}"); // drop the forward
        }
    }

    private static string AndroidPackage(CarbonConfig config) =>
        string.IsNullOrWhiteSpace(config.Bundle.Android.Package)
            ? config.App.Identifier
            : config.Bundle.Android.Package!;

    /// <summary>Polls for the launched app's process id (via <c>pidof</c>) for a few seconds.</summary>
    private static async Task<string?> WaitForAppPid(string adb, string package, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20 && !ct.IsCancellationRequested; attempt++)
        {
            var pid = await MobileBundleSupport.RunCapture(adb, $"shell pidof -s {package}");
            if (!string.IsNullOrWhiteSpace(pid)) return pid.Trim();
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return null; }
        }
        return null;
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
