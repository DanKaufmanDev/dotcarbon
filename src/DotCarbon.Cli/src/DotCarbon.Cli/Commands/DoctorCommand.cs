using System.CommandLine;
using DotCarbon.Cli.Bundling;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon doctor</c> — reports the plugin platform matrix and warns when the app references
/// plugins that don't support a mobile target.
/// </summary>
public static class DoctorCommand
{
    public static Command Build()
    {
        var command = new Command("doctor", "Check plugin platform compatibility across desktop/android/ios");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        command.AddOption(project);
        command.SetHandler(context =>
        {
            var projectDir = context.ParseResult.GetValueForOption(project);
            var workingDir = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var configPath = Path.Combine(workingDir, "carbon.json");
            if (!File.Exists(configPath))
            {
                WriteColor($"[Carbon] No carbon.json found in {workingDir}", ConsoleColor.Red);
                context.ExitCode = 1;
                return;
            }
            _ = ConfigLoader.Load(configPath);
            context.ExitCode = Run(workingDir);
        });
        return command;
    }

    private static int Run(string workingDir)
    {
        WriteColor("\n⚡ Carbon doctor\n", ConsoleColor.Cyan);

        var plugins = PluginCompatibility.Discover(workingDir);
        if (plugins.Count == 0)
        {
            Console.WriteLine("No DotCarbon plugins referenced.");
            return 0;
        }

        Console.WriteLine($"Plugins ({plugins.Count} referenced):");
        foreach (var plugin in plugins)
        {
            Console.Write($"  {plugin.Namespace,-16} ");
            foreach (var platform in PluginCompatibility.Platforms)
            {
                var ok = plugin.Supports(platform);
                Console.Write($"{platform} ");
                WriteColor(ok ? "✓  " : "✗  ", ok ? ConsoleColor.Green : ConsoleColor.Red, newline: false);
            }
            Console.WriteLine();
        }

        var warnings = plugins
            .Where(plugin => PluginCompatibility.Platforms.Any(p => !plugin.Supports(p)))
            .ToList();

        Console.WriteLine();
        if (warnings.Count == 0)
        {
            WriteColor("All referenced plugins support every platform.", ConsoleColor.Green);
            return 0;
        }

        WriteColor("Warnings:", ConsoleColor.Yellow);
        foreach (var plugin in warnings)
        {
            var unsupported = PluginCompatibility.Platforms.Where(p => !plugin.Supports(p));
            WriteColor($"  ⚠ {plugin.Namespace} — not compatible with: {string.Join(", ", unsupported)}", ConsoleColor.Yellow);
        }
        Console.WriteLine("\nUse `carbon bundle <platform> --allow-unsupported-plugins` to bundle anyway.");
        return 0;
    }

    private static void WriteColor(string message, ConsoleColor color, bool newline = true)
    {
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(message);
        else Console.Write(message);
        Console.ResetColor();
    }
}
