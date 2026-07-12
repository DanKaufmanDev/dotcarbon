using System.CommandLine;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class CapabilitiesCommand
{
    public static Command Build()
    {
        var command = new Command("capabilities", "Sync and validate Carbon command capabilities");
        command.AddAlias("capability");
        command.AddCommand(SyncSubcommand());
        command.AddCommand(CheckSubcommand());
        return command;
    }

    private static Command SyncSubcommand()
    {
        var cmd = new Command("sync", "Discover [CarbonCommand] methods and allow them for the main window");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var outFile = new Option<FileInfo?>(
            "--out", "Optional carbon.d.ts output path (default: ui/src/carbon.d.ts)");
        cmd.AddOption(project);
        cmd.AddOption(outFile);
        cmd.SetHandler((projectDir, outPath) =>
        {
            var root = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var result = TypesCommand.Generate(root, outPath?.FullName, syncCapabilities: true);

            WriteColor($"[Carbon] Discovered {result.CommandCount} command(s).", ConsoleColor.Green);
            if (result.CapabilityPath is not null)
                Console.WriteLine($"[Carbon] Synced {result.SyncedCapabilityCount} command(s) -> {Path.GetRelativePath(root, result.CapabilityPath)}");
            Console.WriteLine($"[Carbon] Types -> {Path.GetRelativePath(root, result.TargetPath)}");
        }, project, outFile);
        return cmd;
    }

    private static Command CheckSubcommand()
    {
        var cmd = new Command("check", "Validate capability references and command patterns");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        cmd.AddOption(project);
        cmd.SetHandler(context =>
        {
            var root = context.ParseResult.GetValueForOption(project)?.FullName ?? Directory.GetCurrentDirectory();
            var result = Check(root);

            foreach (var warning in result.Warnings)
                WriteColor($"[Carbon] Warning: {warning}", ConsoleColor.Yellow);
            foreach (var error in result.Errors)
                WriteColor($"[Carbon] Error: {error}", ConsoleColor.Red);

            if (result.Errors.Count == 0)
                WriteColor("[Carbon] Capabilities OK.", ConsoleColor.Green);
            context.ExitCode = result.Errors.Count == 0 ? 0 : 1;
        });
        return cmd;
    }

    internal static CapabilityCheckResult Check(string root)
    {
        var configPath = Path.Combine(root, "carbon.json");
        if (!File.Exists(configPath))
            return new CapabilityCheckResult([], [$"No carbon.json found in {root}"]);

        var config = ConfigLoader.Load(configPath);
        var warnings = new List<string>();
        var errors = new List<string>();
        var knownWindows = new HashSet<string>(StringComparer.Ordinal)
        {
            config.Window.Label
        };
        foreach (var window in config.Windows)
            knownWindows.Add(window.Label);

        var referenced = new HashSet<string>(config.Security.DefaultCapabilities, StringComparer.Ordinal);
        foreach (var capability in config.Window.Capabilities)
            referenced.Add(capability);
        foreach (var window in config.Windows)
            foreach (var capability in window.Capabilities)
                referenced.Add(capability);

        foreach (var capability in referenced.OrderBy(value => value, StringComparer.Ordinal))
            if (!config.Security.Capabilities.ContainsKey(capability))
                errors.Add($"Capability '{capability}' is referenced by a window/defaultCapabilities but is not defined.");

        foreach (var (name, capability) in config.Security.Capabilities.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var label in capability.Windows)
            {
                if (label != "*" && !knownWindows.Contains(label))
                    errors.Add($"Capability '{name}' targets unknown window '{label}'.");
            }

            foreach (var pattern in capability.Commands.Concat(capability.Permissions))
            {
                if (!IsCommandPattern(pattern))
                    errors.Add($"Capability '{name}' has invalid command pattern '{pattern}'.");
            }

            if (capability.Commands.Count == 0 && capability.Permissions.Count == 0)
                warnings.Add($"Capability '{name}' does not allow any commands.");
        }

        if (config.Security.Enabled &&
            config.Security.DefaultCapabilities.Count == 0 &&
            config.Window.Capabilities.Count == 0 &&
            config.Windows.All(window => window.Capabilities.Count == 0) &&
            config.Security.Capabilities.Values.All(capability => capability.Windows.Count == 0))
            warnings.Add("Security is enabled, but no capabilities are attached to any window.");

        return new CapabilityCheckResult(warnings, errors);
    }

    private static bool IsCommandPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        if (value == "*") return true;
        if (value == "core:event_emit") return true;

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == value.Length - 1 || value.IndexOf(':', colon + 1) >= 0)
            return false;

        var ns = value[..colon];
        var command = value[(colon + 1)..];
        return IsIdentifier(ns) && (command == "*" || IsIdentifier(command));
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_');

    private static void WriteColor(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}

internal sealed record CapabilityCheckResult(
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
