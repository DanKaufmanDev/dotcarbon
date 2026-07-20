using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class CapabilitiesCommand
{
    public static Command Build()
    {
        var command = new Command("capabilities", "Sync and validate Carbon command capabilities");
        command.AddAlias("capability");
        command.AddCommand(ListSubcommand());
        command.AddCommand(AddSubcommand());
        command.AddCommand(SyncSubcommand());
        command.AddCommand(CheckSubcommand());
        return command;
    }

    private static Command ListSubcommand()
    {
        var cmd = new Command("list", "List known first-party capability permissions");
        cmd.SetHandler(() =>
        {
            foreach (var permission in CapabilityPermissionCatalog.All)
            {
                Console.WriteLine($"{permission.Id}");
                Console.WriteLine($"  plugin:    {permission.PluginNamespace}");
                Console.WriteLine($"  commands:  {string.Join(", ", permission.Commands)}");
                Console.WriteLine($"  platforms: {string.Join(", ", permission.Platforms)}");
                if (permission.Requirements.Count > 0)
                    Console.WriteLine($"  requires:  {string.Join(", ", permission.Requirements.Select(requirement => requirement.Path))}");
                Console.WriteLine();
            }
        });
        return cmd;
    }

    private static Command AddSubcommand()
    {
        var cmd = new Command("add", "Add a permission to a capability file");
        var permission = new Argument<string>("permission", "Permission id or plugin alias, e.g. fs or fs:default");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var capability = new Option<string>(
            "--capability", getDefaultValue: () => "main", description: "Capability file/name to update");
        var window = new Option<string>(
            "--window", getDefaultValue: () => "main", description: "Window label to attach the capability to");
        cmd.AddArgument(permission);
        cmd.AddOption(project);
        cmd.AddOption(capability);
        cmd.AddOption(window);
        cmd.SetHandler((permissionValue, projectDir, capabilityName, windowLabel) =>
        {
            var root = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            var added = AddPermission(root, permissionValue, capabilityName, windowLabel);
            WriteColor(
                added.Added
                    ? $"[Carbon] Added {added.PermissionId} -> {Path.GetRelativePath(root, added.CapabilityPath)}"
                    : $"[Carbon] {added.PermissionId} is already present in {Path.GetRelativePath(root, added.CapabilityPath)}",
                ConsoleColor.Green);
            foreach (var warning in Check(root).Warnings)
                WriteColor($"[Carbon] Warning: {warning}", ConsoleColor.Yellow);
        }, permission, project, capability, window);
        return cmd;
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

            var permissionIds = capability.Permissions
                .Select(entry => entry.Identifier)
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>()
                .ToList();

            foreach (var pattern in capability.Commands.Concat(permissionIds))
            {
                if (!IsCommandPattern(pattern))
                    errors.Add($"Capability '{name}' has invalid command pattern '{pattern}'.");
            }

            if (capability.Commands.Count == 0 && capability.Permissions.Count == 0)
                warnings.Add($"Capability '{name}' does not allow any commands.");

            warnings.AddRange(CapabilityPermissionCatalog.RequirementWarnings(config, permissionIds));
        }

        if (config.Security.Enabled &&
            config.Security.DefaultCapabilities.Count == 0 &&
            config.Window.Capabilities.Count == 0 &&
            config.Windows.All(window => window.Capabilities.Count == 0) &&
            config.Security.Capabilities.Values.All(capability => capability.Windows.Count == 0))
            warnings.Add("Security is enabled, but no capabilities are attached to any window.");

        return new CapabilityCheckResult(warnings, errors);
    }

    internal static CapabilityAddResult AddPermission(
        string root,
        string permissionValue,
        string capabilityName = "main",
        string windowLabel = "main")
    {
        var configPath = Path.Combine(root, "carbon.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"No carbon.json found in {root}");

        var permission = CapabilityPermissionCatalog.Resolve(permissionValue)
            ?? throw new InvalidOperationException(
                $"Unknown permission '{permissionValue}'. Run `carbon capabilities list` to see known permissions.");
        var permissionId = permission.Id;

        var capabilityDir = Path.Combine(root, "src-carbon", "capabilities");
        Directory.CreateDirectory(capabilityDir);
        var capabilityPath = Path.Combine(capabilityDir, capabilityName + ".json");
        var document = LoadOrCreateCapability(capabilityPath, capabilityName, windowLabel);
        var permissions = GetArray(document, "permissions");
        var added = AddUnique(permissions, permissionId);
        EnsureArrayHasValue(document, "windows", windowLabel);
        SaveJson(document, capabilityPath);

        EnsureCapabilityAttached(configPath, capabilityName, windowLabel);
        return new CapabilityAddResult(permissionId, capabilityPath, added);
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

    private static JsonObject LoadOrCreateCapability(string path, string name, string windowLabel)
    {
        if (File.Exists(path))
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? CreateCapability(name, windowLabel);
            }
            catch (JsonException)
            {
                return CreateCapability(name, windowLabel);
            }
        }

        return CreateCapability(name, windowLabel);
    }

    private static JsonObject CreateCapability(string name, string windowLabel) => new()
    {
        ["identifier"] = name,
        ["description"] = $"{name} window permissions.",
        ["windows"] = new JsonArray(windowLabel),
        ["permissions"] = new JsonArray()
    };

    private static void EnsureCapabilityAttached(string configPath, string capabilityName, string windowLabel)
    {
        var root = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject()
            ?? throw new InvalidOperationException("carbon.json must contain a JSON object.");
        var security = GetObject(root, "security");
        security["enabled"] = true;

        var targetWindow = GetObject(root, "window");
        if (windowLabel != "main")
        {
            var windows = GetArray(root, "windows");
            var existing = windows.OfType<JsonObject>()
                .FirstOrDefault(window =>
                    window["label"]?.GetValue<string>() == windowLabel);
            targetWindow = existing ?? targetWindow;
        }

        EnsureArrayHasValue(targetWindow, "capabilities", capabilityName);
        SaveJson(root, configPath);
    }

    private static JsonObject GetObject(JsonObject root, string property)
    {
        if (root[property] is JsonObject existing) return existing;
        var value = new JsonObject();
        root[property] = value;
        return value;
    }

    private static JsonArray GetArray(JsonObject root, string property)
    {
        if (root[property] is JsonArray existing) return existing;
        var value = new JsonArray();
        root[property] = value;
        return value;
    }

    private static void EnsureArrayHasValue(JsonObject root, string property, string value) =>
        AddUnique(GetArray(root, property), value);

    private static bool AddUnique(JsonArray array, string value)
    {
        if (array.Any(item => item?.GetValue<string>() == value)) return false;
        array.Add(value);
        return true;
    }

    private static void SaveJson(JsonObject root, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

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

internal sealed record CapabilityAddResult(string PermissionId, string CapabilityPath, bool Added);
