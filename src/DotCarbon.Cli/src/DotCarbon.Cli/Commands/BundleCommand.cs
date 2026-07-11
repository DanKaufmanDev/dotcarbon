using System.CommandLine;
using DotCarbon.Cli.Bundling;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon bundle [desktop|android|ios]</c> — one app model, many platform outputs.
/// Desktop is fully wired; android/ios are reserved targets. <c>carbon build</c> stays
/// as the desktop alias and shares the same build engine.
/// </summary>
public static class BundleCommand
{
    public static Command Build()
    {
        var bundle = new Command("bundle", "Package a Carbon app for a platform target");

        bundle.AddCommand(DesktopSubcommand());
        bundle.AddCommand(ReservedSubcommand(new AndroidBundler()));
        bundle.AddCommand(ReservedSubcommand(new IosBundler()));

        // `carbon bundle` with no subcommand → desktop with sensible defaults.
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

        cmd.AddOption(project);
        cmd.AddOption(target);
        cmd.AddOption(aot);
        cmd.AddOption(noPackage);
        cmd.AddOption(updaterArtifacts);
        cmd.AddOption(dryRun);

        cmd.SetHandler(async context =>
        {
            context.ExitCode = await RunDesktop(
                context.ParseResult.GetValueForOption(project),
                context.ParseResult.GetValueForOption(target)!,
                context.ParseResult.GetValueForOption(aot),
                package: !context.ParseResult.GetValueForOption(noPackage),
                updaterArtifacts: context.ParseResult.GetValueForOption(updaterArtifacts),
                dryRun: context.ParseResult.GetValueForOption(dryRun));
        });

        return cmd;
    }

    private static async Task<int> RunDesktop(
        DirectoryInfo? project, string target, bool aot, bool package, bool updaterArtifacts, bool dryRun)
    {
        var workingDir = project?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");
        if (!File.Exists(configPath))
        {
            WriteError($"No carbon.json found in {workingDir}");
            return 1;
        }

        var config = ConfigLoader.Load(configPath);
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

    private static Command ReservedSubcommand(IBundlerTarget bundler)
    {
        var cmd = new Command(bundler.Id, $"Bundle for {bundler.DisplayName} (reserved)");
        var dryRun = new Option<bool>("--dry-run", "Print the bundle plan without executing it");
        cmd.AddOption(dryRun);

        cmd.SetHandler(async context =>
        {
            var ctx = new BundleContext(
                new CarbonConfig(), Directory.GetCurrentDirectory(), null,
                Target: "", Aot: false, Package: true, UpdaterArtifacts: false,
                DryRun: context.ParseResult.GetValueForOption(dryRun));

            if (ctx.DryRun)
            {
                bundler.Plan(ctx).Render(dryRun: true);
                context.ExitCode = 0;
                return;
            }

            context.ExitCode = await bundler.ExecuteAsync(ctx);
        });

        return cmd;
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Carbon] {message}");
        Console.ResetColor();
    }
}
