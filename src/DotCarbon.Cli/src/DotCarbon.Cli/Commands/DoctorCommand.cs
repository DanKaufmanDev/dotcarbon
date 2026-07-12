using System.CommandLine;
using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Platforms;
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
            context.ExitCode = Run(ConfigLoader.Load(configPath), workingDir);
        });
        return command;
    }

    private static int Run(CarbonConfig config, string workingDir)
    {
        WriteColor("\n⚡ Carbon doctor\n", ConsoleColor.Cyan);

        var warnings = new List<string>();
        ReportPlugins(workingDir, warnings);
        ReportPermissions(config, workingDir, warnings);

        Console.WriteLine();
        if (warnings.Count == 0)
        {
            WriteColor("No issues found.", ConsoleColor.Green);
            return 0;
        }

        WriteColor("Warnings:", ConsoleColor.Yellow);
        foreach (var warning in warnings) WriteColor($"  ⚠ {warning}", ConsoleColor.Yellow);
        return 0;
    }

    private static void ReportPlugins(string workingDir, List<string> warnings)
    {
        var plugins = PluginCompatibility.Discover(workingDir);
        if (plugins.Count == 0)
        {
            Console.WriteLine("Plugins: none referenced.\n");
            return;
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

            var unsupported = PluginCompatibility.Platforms.Where(p => !plugin.Supports(p)).ToList();
            if (unsupported.Count > 0)
                warnings.Add($"plugin '{plugin.Namespace}' is not compatible with: {string.Join(", ", unsupported)} " +
                             "(use `carbon bundle <platform> --allow-unsupported-plugins`).");
        }
        Console.WriteLine();
    }

    private static void ReportPermissions(CarbonConfig config, string workingDir, List<string> warnings)
    {
        var enabled = PermissionCatalog.Enabled(config).ToList();
        if (enabled.Count == 0)
        {
            Console.WriteLine("Permissions: none requested.");
            return;
        }

        Console.WriteLine($"Permissions ({enabled.Count} requested):");
        foreach (var mapping in enabled)
        {
            var android = string.Join(", ", mapping.AndroidPermissions.Select(p => p.Replace("android.permission.", "")));
            var ios = mapping.IosUsageKey ?? "(runtime only)";
            Console.WriteLine($"  {mapping.Id,-14} android: {android,-28} ios: {ios}");

            if (mapping.IosUsageKey is not null &&
                !(config.Permissions.Descriptions.TryGetValue(mapping.Id, out var d) && !string.IsNullOrWhiteSpace(d)))
                warnings.Add($"permission '{mapping.Id}' has no custom iOS usage string — using the default " +
                             $"(set permissions.descriptions.{mapping.Id} before App Store submission).");
        }

        if (string.Equals(config.Permissions.Files, "external", StringComparison.OrdinalIgnoreCase))
            warnings.Add("permissions.files = \"external\" grants broad storage access — prefer \"appData\" or \"documents\" where possible.");

        var mobileAdded = PlatformService.KnownIds.Where(id => id is "android" or "ios")
            .Any(id => Directory.Exists(PlatformService.PlatformDir(workingDir, id)));
        if (!mobileAdded)
            warnings.Add("permissions are declared but no mobile platform is added (`carbon platform add android|ios`).");
    }

    private static void WriteColor(string message, ConsoleColor color, bool newline = true)
    {
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(message);
        else Console.Write(message);
        Console.ResetColor();
    }
}
