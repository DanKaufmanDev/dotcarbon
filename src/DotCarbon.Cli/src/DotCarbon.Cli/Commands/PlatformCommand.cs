using System.CommandLine;
using DotCarbon.Cli.Platforms;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon platform add|sync|list</c> — generate and maintain native platform shells
/// (android, ios, desktop) under <c>.carbon/platforms/</c>, with edit-preserving regeneration.
/// </summary>
public static class PlatformCommand
{
    public static Command Build()
    {
        var platform = new Command("platform",
            "Generate and manage native platform shells (android, ios, desktop)");
        platform.AddCommand(AddSubcommand());
        platform.AddCommand(SyncSubcommand());
        platform.AddCommand(ListSubcommand());
        return platform;
    }

    private static Option<DirectoryInfo?> ProjectOption() =>
        new("--project", "Path to the Carbon project (default: current directory)");

    private static Argument<string> PlatformArgument() =>
        new("platform", "Target platform: android, ios, or desktop");

    private static Command AddSubcommand()
    {
        var cmd = new Command("add", "Generate a native shell under .carbon/platforms/<platform>");
        var platformArg = PlatformArgument();
        var project = ProjectOption();
        cmd.AddArgument(platformArg);
        cmd.AddOption(project);
        cmd.SetHandler(context =>
        {
            if (!TryLoad(context.ParseResult.GetValueForOption(project), out var config, out var workingDir))
            {
                context.ExitCode = 1;
                return;
            }
            context.ExitCode = PlatformService.Add(
                config, workingDir, context.ParseResult.GetValueForArgument(platformArg));
        });
        return cmd;
    }

    private static Command SyncSubcommand()
    {
        var cmd = new Command("sync", "Regenerate a platform shell, preserving your manual edits");
        var platformArg = PlatformArgument();
        var project = ProjectOption();
        var force = new Option<bool>("--force", "Overwrite manually edited managed files");
        cmd.AddArgument(platformArg);
        cmd.AddOption(project);
        cmd.AddOption(force);
        cmd.SetHandler(context =>
        {
            if (!TryLoad(context.ParseResult.GetValueForOption(project), out var config, out var workingDir))
            {
                context.ExitCode = 1;
                return;
            }
            context.ExitCode = PlatformService.Sync(
                config, workingDir,
                context.ParseResult.GetValueForArgument(platformArg),
                context.ParseResult.GetValueForOption(force));
        });
        return cmd;
    }

    private static Command ListSubcommand()
    {
        var cmd = new Command("list", "List added platforms and whether each is up to date");
        var project = ProjectOption();
        cmd.AddOption(project);
        cmd.SetHandler(context =>
        {
            if (!TryLoad(context.ParseResult.GetValueForOption(project), out var config, out var workingDir))
            {
                context.ExitCode = 1;
                return;
            }
            context.ExitCode = PlatformService.List(config, workingDir);
        });
        return cmd;
    }

    private static bool TryLoad(DirectoryInfo? project, out CarbonConfig config, out string workingDir)
    {
        workingDir = project?.FullName ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(workingDir, "carbon.json");
        if (!File.Exists(configPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Carbon] No carbon.json found in {workingDir}");
            Console.ResetColor();
            config = new CarbonConfig();
            return false;
        }
        config = ConfigLoader.Load(configPath);
        return true;
    }
}
