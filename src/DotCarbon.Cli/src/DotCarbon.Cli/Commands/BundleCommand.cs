using System.CommandLine;
using DotCarbon.Cli.Bundling;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// Registers the platform-specific <c>carbon bundle</c> commands.
/// </summary>
public static class BundleCommand
{
    public static Command Build()
    {
        var bundle = new Command("bundle", "Package a Carbon app for a platform target");

        bundle.AddCommand(DesktopSubcommand());
        bundle.AddCommand(AndroidSubcommand());
        bundle.AddCommand(IosSubcommand());

        // Preserve the convenient desktop default when no target is supplied.
        bundle.SetHandler(async context =>
        {
            context.ExitCode = await RunDesktop(
                project: null,
                target: BuildCommand.GetDefaultTarget(),
                aot: false,
                package: true,
                updaterArtifacts: false,
                dryRun: false);
        });

        return bundle;
    }

    private static Command DesktopSubcommand()
    {
        var cmd = new Command("desktop", "Bundle for macOS, Windows, or Linux");

        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var target = new Option<string>(
            "--target",
            getDefaultValue: BuildCommand.GetDefaultTarget,
            description: "Runtime target (osx-arm64, osx-x64, osx-universal, win-x64, linux-x64, …)");
        var aot = new Option<bool>(
            "--aot", "Use NativeAOT (experimental; Photino's native library remains a second file)");
        var noPackage = new Option<bool>(
            "--no-package", "Publish the self-contained executable only; skip the installer");
        var updaterArtifacts = new Option<bool>(
            "--updater-artifacts", "Create and sign updater metadata (requires CARBON_UPDATER_PRIVATE_KEY)");
        var dryRun = new Option<bool>(
            "--dry-run", "Print the bundle plan without executing it");
        var verify = new Option<bool>(
            "--verify", "Validate the existing out/<target> publish output without bundling");

        cmd.AddOption(project);
        cmd.AddOption(target);
        cmd.AddOption(aot);
        cmd.AddOption(noPackage);
        cmd.AddOption(updaterArtifacts);
        cmd.AddOption(dryRun);
        cmd.AddOption(verify);

        cmd.SetHandler(async context =>
        {
            context.ExitCode = await RunDesktop(
                context.ParseResult.GetValueForOption(project),
                context.ParseResult.GetValueForOption(target)!,
                context.ParseResult.GetValueForOption(aot),
                package: !context.ParseResult.GetValueForOption(noPackage),
                updaterArtifacts: context.ParseResult.GetValueForOption(updaterArtifacts),
                dryRun: context.ParseResult.GetValueForOption(dryRun),
                verify: context.ParseResult.GetValueForOption(verify));
        });

        return cmd;
    }

    private static Command AndroidSubcommand()
    {
        var cmd = new Command("android", "Bundle for Android (APK/AAB via .NET Android)");

        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var aab = new Option<bool>("--aab", "Produce an AAB (Play Store bundle) instead of an APK");
        var apk = new Option<bool>("--apk", "Produce an APK (the default)");
        var debug = new Option<bool>("--debug", "Build the Debug configuration");
        var release = new Option<bool>("--release", "Build the Release configuration (the default)");
        var dryRun = new Option<bool>("--dry-run", "Print the bundle plan without executing it");
        var allowUnsupported = new Option<bool>(
            "--allow-unsupported-plugins", "Bundle even if referenced plugins do not support Android");

        cmd.AddOption(project);
        cmd.AddOption(aab);
        cmd.AddOption(apk);
        cmd.AddOption(debug);
        cmd.AddOption(release);
        cmd.AddOption(dryRun);
        cmd.AddOption(allowUnsupported);

        cmd.SetHandler(async context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                WriteError($"No carbon.json found in {workingDir}");
                context.ExitCode = 1;
                return;
            }

            var format = context.ParseResult.GetValueForOption(aab) ? "aab" : "apk";
            var isRelease = !context.ParseResult.GetValueForOption(debug);
            var config = ConfigLoader.Load(configPath);
            context.ExitCode = await new AndroidBundler().ExecuteAsync(
                config, workingDir, format, isRelease,
                context.ParseResult.GetValueForOption(dryRun),
                context.ParseResult.GetValueForOption(allowUnsupported));
        });

        return cmd;
    }

    private static Command IosSubcommand()
    {
        var cmd = new Command("ios", "Bundle for iOS (simulator/device/archive via .NET iOS)");

        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var simulator = new Option<bool>("--simulator", "Build for the iOS simulator (the default)");
        var device = new Option<bool>("--device", "Build for a physical device (needs signing)");
        var archive = new Option<bool>("--archive", "Produce a signed .ipa archive for distribution");
        var dryRun = new Option<bool>("--dry-run", "Print the bundle plan without executing it");
        var allowUnsupported = new Option<bool>(
            "--allow-unsupported-plugins", "Bundle even if referenced plugins do not support iOS");

        cmd.AddOption(project);
        cmd.AddOption(simulator);
        cmd.AddOption(device);
        cmd.AddOption(archive);
        cmd.AddOption(dryRun);
        cmd.AddOption(allowUnsupported);

        cmd.SetHandler(async context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                WriteError($"No carbon.json found in {workingDir}");
                context.ExitCode = 1;
                return;
            }

            var mode = context.ParseResult.GetValueForOption(archive) ? "archive"
                : context.ParseResult.GetValueForOption(device) ? "device"
                : "simulator";
            var config = ConfigLoader.Load(configPath);
            context.ExitCode = await new IosBundler().ExecuteAsync(
                config, workingDir, mode,
                context.ParseResult.GetValueForOption(dryRun),
                context.ParseResult.GetValueForOption(allowUnsupported));
        });

        return cmd;
    }

    private static async Task<int> RunDesktop(
        DirectoryInfo? project, string target, bool aot, bool package, bool updaterArtifacts, bool dryRun, bool verify = false)
    {
        var workingDir = project?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");
        if (!File.Exists(configPath))
        {
            WriteError($"No carbon.json found in {workingDir}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
        if (verify)
            return VerifyDesktopOutput(workingDir, target, aot);

        var context = new BundleContext(
            config, workingDir, project, target, aot, package, updaterArtifacts, dryRun);

        var bundler = new DesktopBundler();
        if (dryRun)
        {
            bundler.Plan(context).Render(dryRun: true);
            return 0;
        }

        return await bundler.ExecuteAsync(context);
    }

    private static int VerifyDesktopOutput(string workingDir, string target, bool aot)
    {
        var outputDir = Path.Combine(workingDir, "out", target);
        var result = PublishOutputVerifier.Verify(outputDir, target, allowSidecars: aot);
        if (!result.Success)
        {
            WriteError(result.Error ?? "Publish output validation failed.");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Carbon] Publish output verified -> {Path.GetRelativePath(workingDir, result.ExecutablePath!)}");
        Console.ResetColor();
        return 0;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
