using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

public static class AddCommand
{
    private static readonly IReadOnlyDictionary<string, PluginDefinition> Plugins =
        new Dictionary<string, PluginDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["biometric"] = new(
                ["biometric", "biometrics", "faceid", "touchid", "DotCarbon.Plugins.Biometric"],
                "DotCarbon.Plugins.Biometric",
                "@dotcarbon/plugin-biometric",
                "DotCarbon.Plugins.Biometric",
                "BiometricPlugin",
                "biometric",
                ["biometric:*"],
                Platforms: ["android", "ios"]),
            ["geolocation"] = new(
                ["geolocation", "geo", "location", "DotCarbon.Plugins.Geolocation"],
                "DotCarbon.Plugins.Geolocation",
                "@dotcarbon/plugin-geolocation",
                "DotCarbon.Plugins.Geolocation",
                "GeolocationPlugin",
                "geolocation",
                ["geolocation:*"],
                Platforms: ["android", "ios"]),
            ["permissions"] = new(
                ["permissions", "permission", "DotCarbon.Plugins.Permissions"],
                "DotCarbon.Plugins.Permissions",
                "@dotcarbon/plugin-permissions",
                "DotCarbon.Plugins.Permissions",
                "PermissionsPlugin",
                "permissions",
                ["permissions:*"],
                Platforms: ["desktop", "android", "ios"]),
            ["haptics"] = new(
                ["haptics", "haptic", "vibrate", "vibration", "DotCarbon.Plugins.Haptics"],
                "DotCarbon.Plugins.Haptics",
                "@dotcarbon/plugin-haptics",
                "DotCarbon.Plugins.Haptics",
                "HapticsPlugin",
                "haptics",
                ["haptics:*"],
                Platforms: ["android", "ios"]),
            ["battery"] = new(
                ["battery", "power", "DotCarbon.Plugins.Battery"],
                "DotCarbon.Plugins.Battery",
                "@dotcarbon/plugin-battery",
                "DotCarbon.Plugins.Battery",
                "BatteryPlugin",
                "battery",
                ["battery:*"],
                Platforms: ["desktop", "android", "ios"]),
            ["autostart"] = new(
                ["autostart", "auto-start", "launch-at-login", "startup", "DotCarbon.Plugins.Autostart"],
                "DotCarbon.Plugins.Autostart",
                "@dotcarbon/plugin-autostart",
                "DotCarbon.Plugins.Autostart",
                "AutostartPlugin",
                "autostart",
                ["autostart:*"],
                Platforms: ["desktop"]),
            ["cli"] = new(
                ["cli", "args", "arguments", "DotCarbon.Plugins.Cli"],
                "DotCarbon.Plugins.Cli",
                "@dotcarbon/plugin-cli",
                "DotCarbon.Plugins.Cli",
                "CliPlugin",
                "cli",
                ["cli:*"],
                Platforms: ["desktop"]),
            ["positioner"] = new(
                ["positioner", "position", "window-position", "DotCarbon.Plugins.Positioner"],
                "DotCarbon.Plugins.Positioner",
                "@dotcarbon/plugin-positioner",
                "DotCarbon.Plugins.Positioner",
                "PositionerPlugin",
                "positioner",
                ["positioner:*"],
                Platforms: ["desktop"]),
            ["clipboard"] = new(
                ["clipboard", "clip", "DotCarbon.Plugins.Clipboard"],
                "DotCarbon.Plugins.Clipboard",
                "@dotcarbon/plugin-clipboard",
                "DotCarbon.Plugins.Clipboard",
                "ClipboardPlugin",
                "clipboard",
                ["clipboard:*"],
                Platforms: ["desktop", "android", "ios"]),
            ["deep-link"] = new(
                ["deep-link", "deeplink", "deep-links", "protocol", "protocols", "DotCarbon.Plugins.DeepLink"],
                "DotCarbon.Plugins.DeepLink",
                "@dotcarbon/plugin-deep-link",
                "DotCarbon.Plugins.DeepLink",
                "DeepLinkPlugin",
                "deep-link",
                ["deep-link:*"],
                Platforms: ["desktop", "android", "ios"]),
            ["dialog"] = new(
                ["dialog", "dialogs", "DotCarbon.Plugins.Dialog"],
                "DotCarbon.Plugins.Dialog",
                "@dotcarbon/plugin-dialog",
                "DotCarbon.Plugins.Dialog",
                "DialogPlugin",
                "dialog",
                ["dialog:*"],
                // Dialogs now come from the host (ICarbonDialogs), so this registers like any other
                // plugin — no Photino window needs threading through a window factory.
                Platforms: ["desktop", "android", "ios"]),
            ["fs"] = new(
                ["fs", "filesystem", "file-system", "DotCarbon.Plugins.FileSystem"],
                "DotCarbon.Plugins.FileSystem",
                "@dotcarbon/plugin-fs",
                "DotCarbon.Plugins.FileSystem",
                "FileSystemPlugin",
                "fs",
                ["fs:*"]),
            ["http"] = new(
                ["http", "fetch", "DotCarbon.Plugins.Http"],
                "DotCarbon.Plugins.Http",
                "@dotcarbon/plugin-http",
                "DotCarbon.Plugins.Http",
                "HttpPlugin",
                "http",
                ["http:*"]),
            ["global-shortcut"] = new(
                ["global-shortcut", "globalshortcut", "shortcut", "shortcuts", "hotkey", "hotkeys", "DotCarbon.Plugins.GlobalShortcut"],
                "DotCarbon.Plugins.GlobalShortcut",
                "@dotcarbon/plugin-global-shortcut",
                "DotCarbon.Plugins.GlobalShortcut",
                "GlobalShortcutPlugin",
                "global-shortcut",
                ["global-shortcut:*"],
                Platforms: ["desktop"]),
            ["notification"] = new(
                ["notification", "notifications", "notify", "DotCarbon.Plugins.Notification"],
                "DotCarbon.Plugins.Notification",
                "@dotcarbon/plugin-notification",
                "DotCarbon.Plugins.Notification",
                "NotificationPlugin",
                "notification",
                ["notification:*"],
                Platforms: ["desktop", "android", "ios"]),
            ["os"] = new(
                ["os", "system", "DotCarbon.Plugins.Os"],
                "DotCarbon.Plugins.Os",
                "@dotcarbon/plugin-os",
                "DotCarbon.Plugins.Os",
                "OsPlugin",
                "os",
                ["os:*"]),
            ["window-state"] = new(
                ["window-state", "windowstate", "window_state", "DotCarbon.Plugins.WindowState"],
                "DotCarbon.Plugins.WindowState",
                "@dotcarbon/plugin-window-state",
                "DotCarbon.Plugins.WindowState",
                "WindowStatePlugin",
                "window-state",
                ["window-state:*"],
                Platforms: ["desktop"]),
            ["localhost"] = new(
                ["localhost", "local-server", "http-server", "DotCarbon.Plugins.Localhost"],
                "DotCarbon.Plugins.Localhost",
                "@dotcarbon/plugin-localhost",
                "DotCarbon.Plugins.Localhost",
                "LocalhostPlugin",
                "localhost",
                ["localhost:*"],
                Platforms: ["desktop"]),
            ["log"] = new(
                ["log", "logger", "logging", "DotCarbon.Plugins.Log"],
                "DotCarbon.Plugins.Log",
                "@dotcarbon/plugin-log",
                "DotCarbon.Plugins.Log",
                "LogPlugin",
                "log",
                ["log:*"]),
            ["path"] = new(
                ["path", "paths", "dirs", "DotCarbon.Plugins.Path"],
                "DotCarbon.Plugins.Path",
                "@dotcarbon/plugin-path",
                "DotCarbon.Plugins.Path",
                "PathPlugin",
                "path",
                ["path:*"]),
            ["persisted-scope"] = new(
                ["persisted-scope", "persistedscope", "persist-scope", "scope-persistence", "DotCarbon.Plugins.PersistedScope"],
                "DotCarbon.Plugins.PersistedScope",
                "@dotcarbon/plugin-persisted-scope",
                "DotCarbon.Plugins.PersistedScope",
                "PersistedScopePlugin",
                "persisted-scope",
                ["persisted-scope:*"],
                Platforms: ["desktop"]),
            ["process"] = new(
                ["process", "proc", "exit", "relaunch", "DotCarbon.Plugins.Process"],
                "DotCarbon.Plugins.Process",
                "@dotcarbon/plugin-process",
                "DotCarbon.Plugins.Process",
                "ProcessPlugin",
                "process",
                ["process:*"],
                Platforms: ["desktop"]),
            ["opener"] = new(
                ["opener", "open", "launcher", "DotCarbon.Plugins.Opener"],
                "DotCarbon.Plugins.Opener",
                "@dotcarbon/plugin-opener",
                "DotCarbon.Plugins.Opener",
                "OpenerPlugin",
                "opener",
                ["opener:*"],
                Platforms: ["desktop"]),
            ["sql"] = new(
                ["sql", "sqlite", "database", "db", "DotCarbon.Plugins.Sql"],
                "DotCarbon.Plugins.Sql",
                "@dotcarbon/plugin-sql",
                "DotCarbon.Plugins.Sql",
                "SqlPlugin",
                "sql",
                ["sql:*"],
                Platforms: ["desktop"]),
            ["secure-storage"] = new(
                ["secure-storage", "securestorage", "keychain", "secrets", "credentials", "DotCarbon.Plugins.SecureStorage"],
                "DotCarbon.Plugins.SecureStorage",
                "@dotcarbon/plugin-secure-storage",
                "DotCarbon.Plugins.SecureStorage",
                "SecureStoragePlugin",
                "secure-storage",
                ["secure-storage:*"],
                Platforms: ["desktop"]),
            ["shell"] = new(
                ["shell", "DotCarbon.Plugins.Shell"],
                "DotCarbon.Plugins.Shell",
                "@dotcarbon/plugin-shell",
                "DotCarbon.Plugins.Shell",
                "ShellPlugin",
                "shell",
                ["shell:*"],
                Platforms: ["desktop"]),
            ["single-instance"] = new(
                ["single-instance", "singleinstance", "single", "instance", "DotCarbon.Plugins.SingleInstance"],
                "DotCarbon.Plugins.SingleInstance",
                "@dotcarbon/plugin-single-instance",
                "DotCarbon.Plugins.SingleInstance",
                "SingleInstancePlugin",
                "single-instance",
                ["single-instance:*"],
                Platforms: ["desktop"]),
            ["store"] = new(
                ["store", "settings", "kv", "DotCarbon.Plugins.Store"],
                "DotCarbon.Plugins.Store",
                "@dotcarbon/plugin-store",
                "DotCarbon.Plugins.Store",
                "StorePlugin",
                "store",
                ["store:*"]),
            ["upload"] = new(
                ["upload", "uploads", "download", "transfer", "DotCarbon.Plugins.Upload"],
                "DotCarbon.Plugins.Upload",
                "@dotcarbon/plugin-upload",
                "DotCarbon.Plugins.Upload",
                "UploadPlugin",
                "upload",
                ["upload:*"],
                Platforms: ["desktop"]),
            ["updater"] = new(
                ["updater", "update", "updates", "DotCarbon.Plugins.Updater"],
                "DotCarbon.Plugins.Updater",
                "@dotcarbon/plugin-updater",
                "DotCarbon.Plugins.Updater",
                "UpdaterPlugin",
                "updater",
                ["updater:*"],
                Platforms: ["desktop"]),
            ["websocket"] = new(
                ["websocket", "ws", "web-socket", "sockets", "DotCarbon.Plugins.WebSocket"],
                "DotCarbon.Plugins.WebSocket",
                "@dotcarbon/plugin-websocket",
                "DotCarbon.Plugins.WebSocket",
                "WebSocketPlugin",
                "websocket",
                ["websocket:*"],
                Platforms: ["desktop"]),
            ["window"] = new(
                ["window", "windows", "webview", "DotCarbon.Plugins.Window"],
                "DotCarbon.Plugins.Window",
                "@dotcarbon/plugin-window",
                "DotCarbon.Plugins.Window",
                "WindowPlugin",
                "window",
                ["window:*"],
                Platforms: ["desktop"]),
        };

    public static Command Build()
    {
        var command = new Command("add", "Add NuGet packages or DotCarbon plugins");
        command.AddCommand(BuildNuget());
        command.AddCommand(BuildPlugin());
        return command;
    }

    private static Command BuildNuget()
    {
        var command = new Command("nuget", "Add any NuGet package to the C# backend");
        var packageArgument = new Argument<string>("package", "NuGet package id, e.g. SharpHook");
        var projectOption = new Option<DirectoryInfo?>(
            "--project",
            "Path to the Carbon project (default: current directory)");
        var versionOption = new Option<string?>(
            "--version",
            "Optional NuGet package version");

        command.AddArgument(packageArgument);
        command.AddOption(projectOption);
        command.AddOption(versionOption);
        command.SetHandler((package, project, version) =>
        {
            var workingDir = project?.FullName ?? Directory.GetCurrentDirectory();
            var config = LoadConfig(workingDir);
            var hostProject = FindHostProject(workingDir, config);
            AddPackageReference(hostProject, package, version);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Carbon] Added NuGet package {package} -> {Path.GetRelativePath(workingDir, hostProject)}");
            Console.ResetColor();
            Console.WriteLine("[Carbon] Use it from your C# commands/plugins, then expose only the safe methods with [CarbonCommand].");
        }, packageArgument, projectOption, versionOption);

        return command;
    }

    private static Command BuildPlugin()
    {
        var command = new Command("plugin", "Add a DotCarbon plugin package");
        var pluginArgument = new Argument<string>("plugin", "Plugin alias or package id, e.g. Notification");
        var projectOption = new Option<DirectoryInfo?>(
            "--project",
            "Path to the Carbon project (default: current directory)");
        var versionOption = new Option<string?>(
            "--version",
            "Optional NuGet package version");
        var noNpmOption = new Option<bool>(
            "--no-npm",
            "Do not add the matching frontend npm package");
        var usingOption = new Option<string?>(
            "--using",
            "C# namespace for a third-party plugin package");
        var classOption = new Option<string?>(
            "--class",
            "Plugin class name for a third-party plugin package");
        var namespaceOption = new Option<string?>(
            "--namespace",
            "DotCarbon plugin namespace/capability id for a third-party plugin package");
        var npmOption = new Option<string?>(
            "--npm",
            "Optional matching frontend npm package for a third-party plugin package");
        var commandOption = new Option<string[]>(
            "--command",
            "Capability command pattern for a third-party plugin package, e.g. example:*")
        {
            AllowMultipleArgumentsPerToken = true,
        };

        command.AddArgument(pluginArgument);
        command.AddOption(projectOption);
        command.AddOption(versionOption);
        command.AddOption(noNpmOption);
        command.AddOption(usingOption);
        command.AddOption(classOption);
        command.AddOption(namespaceOption);
        command.AddOption(npmOption);
        command.AddOption(commandOption);
        command.SetHandler((context) =>
        {
            var plugin = context.ParseResult.GetValueForArgument(pluginArgument);
            var project = context.ParseResult.GetValueForOption(projectOption);
            var version = context.ParseResult.GetValueForOption(versionOption);
            var noNpm = context.ParseResult.GetValueForOption(noNpmOption);
            var workingDir = project?.FullName ?? Directory.GetCurrentDirectory();
            var definition = ResolvePlugin(
                plugin,
                context.ParseResult.GetValueForOption(usingOption),
                context.ParseResult.GetValueForOption(classOption),
                context.ParseResult.GetValueForOption(namespaceOption),
                NormalizeNpmPackage(context.ParseResult.GetValueForOption(npmOption)),
                context.ParseResult.GetValueForOption(commandOption) ?? []);
            var config = LoadConfig(workingDir);
            var hostProject = FindHostProject(workingDir, config);

            AddPackageReference(hostProject, definition.NuGetPackage, version);
            UpdateProgramCs(hostProject, definition);
            AddCapability(workingDir, definition);
            UpdateCarbonJson(workingDir, definition);
            if (!noNpm && definition.NpmPackage is not null)
                AddNpmPackage(workingDir, config, definition.NpmPackage);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Carbon] Added plugin {definition.NuGetPackage}");
            Console.ResetColor();
            Console.WriteLine($"[Carbon] Backend: .UsePlugin<{definition.ClassName}>()");
            if (definition.NpmPackage is not null && !noNpm)
                Console.WriteLine($"[Carbon] Frontend package: {definition.NpmPackage}");
            Console.WriteLine($"[Carbon] Capability: src-carbon/capabilities/{definition.Namespace}.json");
        });

        return command;
    }

    private static CarbonConfig LoadConfig(string workingDir)
    {
        var configPath = Path.Combine(workingDir, "carbon.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"No carbon.json found in {workingDir}");
        return ConfigLoader.Load(configPath);
    }

    private static string FindHostProject(string workingDir, CarbonConfig config) =>
        ProjectLocator.FindHostProject(workingDir, config)
        ?? throw new InvalidOperationException(
            "Could not identify the executable host project. Set build.backendProject in carbon.json.");

    private static PluginDefinition ResolvePlugin(
        string value,
        string? usingNamespace,
        string? className,
        string? pluginNamespace,
        string? npmPackage,
        IReadOnlyList<string> commands)
    {
        foreach (var definition in Plugins.Values)
            if (definition.Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase)))
                return definition;

        var inferredClass = className ?? InferPluginClassName(value);
        var inferredNamespace = pluginNamespace ?? InferPluginNamespace(value);
        return new PluginDefinition(
            [value],
            value,
            npmPackage,
            usingNamespace ?? value,
            inferredClass,
            inferredNamespace,
            commands.Count == 0 ? [$"{inferredNamespace}:*"] : commands);
    }

    private static void AddPackageReference(string projectPath, string package, string? version)
    {
        var document = XDocument.Load(projectPath);
        var root = document.Root ?? throw new InvalidOperationException($"Invalid project file: {projectPath}");

        var existing = root.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals((string?)element.Attribute("Include"), package, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(version))
                existing.SetAttributeValue("Version", version);
            SaveProject(document, projectPath);
            return;
        }

        var itemGroup = root.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ItemGroup" &&
                element.Elements().Any(child => child.Name.LocalName == "PackageReference"))
            ?? new XElement(root.GetDefaultNamespace() + "ItemGroup");
        if (itemGroup.Parent is null) root.Add(itemGroup);

        var reference = new XElement(root.GetDefaultNamespace() + "PackageReference",
            new XAttribute("Include", package));
        if (!string.IsNullOrWhiteSpace(version))
            reference.SetAttributeValue("Version", version);
        itemGroup.Add(reference);
        SaveProject(document, projectPath);
    }

    private static void SaveProject(XDocument document, string projectPath)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
        };
        using var writer = XmlWriter.Create(projectPath, settings);
        document.Save(writer);
    }

    private static void UpdateProgramCs(string hostProject, PluginDefinition definition)
    {
        var programPath = Path.Combine(Path.GetDirectoryName(hostProject)!, "Program.cs");
        if (!File.Exists(programPath))
            throw new FileNotFoundException($"Program.cs not found next to host project: {hostProject}");

        var text = File.ReadAllText(programPath);
        if (!Regex.IsMatch(text, $"^using\\s+{Regex.Escape(definition.UsingNamespace)}\\s*;", RegexOptions.Multiline))
            text = $"using {definition.UsingNamespace};{Environment.NewLine}" + text;

        var usePluginLine = definition.WindowPluginFactory is null
            ? $".UsePlugin<{definition.ClassName}>()"
            : $".WithWindowPlugin({definition.WindowPluginFactory})";
        if (text.Contains(usePluginLine, StringComparison.Ordinal))
        {
            File.WriteAllText(programPath, text);
            return;
        }

        var runIndex = text.IndexOf(".Run();", StringComparison.Ordinal);
        if (runIndex < 0)
            throw new InvalidOperationException("Could not find .Run(); in Program.cs.");

        text = text.Insert(runIndex, $"{usePluginLine}{Environment.NewLine}    ");
        File.WriteAllText(programPath, text);
    }

    private static void AddCapability(string workingDir, PluginDefinition definition)
    {
        var dir = Path.Combine(workingDir, "src-carbon", "capabilities");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, definition.Namespace + ".json");
        if (File.Exists(path)) return;

        var capability = new JsonObject
        {
            ["description"] = $"{definition.NuGetPackage} permissions.",
            ["windows"] = new JsonArray("main")
        };
        var permission = CapabilityPermissionCatalog.Resolve(definition.Namespace);
        if (permission is not null)
            capability["permissions"] = new JsonArray(permission.Id);
        else
            capability["commands"] = new JsonArray(definition.Commands.Select(command => JsonValue.Create(command)).ToArray<JsonNode?>());
        File.WriteAllText(path, capability.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void UpdateCarbonJson(string workingDir, PluginDefinition definition)
    {
        var path = Path.Combine(workingDir, "carbon.json");
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("carbon.json must contain a JSON object.");

        var window = GetObject(root, "window");
        var capabilities = GetArray(window, "capabilities");
        AddUnique(capabilities, definition.Namespace);

        var security = GetObject(root, "security");
        security["enabled"] = true;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void AddNpmPackage(string workingDir, CarbonConfig config, string npmPackage)
    {
        var frontendDist = Path.GetFullPath(Path.Combine(workingDir, config.Build.FrontendDist));
        var uiDir = FindPackageJson(Path.GetDirectoryName(frontendDist) ?? workingDir) ?? workingDir;
        var packagePath = Path.Combine(uiDir, "package.json");
        if (!File.Exists(packagePath)) return;

        var root = JsonNode.Parse(File.ReadAllText(packagePath))?.AsObject()
            ?? throw new InvalidOperationException("package.json must contain a JSON object.");
        var dependencies = GetObject(root, "dependencies");
        if (!dependencies.ContainsKey(npmPackage))
            dependencies[npmPackage] = "^0.1.0";
        File.WriteAllText(packagePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static string? FindPackageJson(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
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

    private static void AddUnique(JsonArray array, string value)
    {
        if (array.Any(item => item?.GetValue<string>() == value)) return;
        array.Add(value);
    }

    private static string InferPluginClassName(string package)
    {
        var tail = package.Split('.', '-', '_').LastOrDefault(part => !string.IsNullOrWhiteSpace(part))
            ?? "Plugin";
        var cleaned = CleanIdentifier(tail);
        return cleaned.EndsWith("Plugin", StringComparison.Ordinal)
            ? cleaned
            : cleaned + "Plugin";
    }

    private static string? NormalizeNpmPackage(string? npmPackage)
    {
        if (string.IsNullOrWhiteSpace(npmPackage)) return npmPackage;
        if (npmPackage.StartsWith("@@", StringComparison.Ordinal)) return npmPackage[1..];
        if (!npmPackage.StartsWith('@') && npmPackage.Contains('/')) return "@" + npmPackage;
        return npmPackage;
    }

    private static string InferPluginNamespace(string package)
    {
        var tail = package.Split('.', '-', '_').LastOrDefault(part => !string.IsNullOrWhiteSpace(part))
            ?? "plugin";
        tail = tail.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase)
            ? tail[..^"Plugin".Length]
            : tail;
        var chars = tail
            .Select(char.ToLowerInvariant)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray();
        return chars.Length == 0 ? "plugin" : new string(chars);
    }

    private static string CleanIdentifier(string value)
    {
        var parts = Regex.Split(value, "[^A-Za-z0-9]+")
            .Where(part => part.Length > 0)
            .ToArray();
        var identifier = string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..]));
        if (identifier.Length == 0) return "Plugin";
        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    /// <summary>First-party plugin catalog, exposed for `carbon doctor` and the bundler compat gate.</summary>
    internal static IReadOnlyDictionary<string, PluginDefinition> Catalog => Plugins;

    internal sealed record PluginDefinition(
        IReadOnlyList<string> Aliases,
        string NuGetPackage,
        string? NpmPackage,
        string UsingNamespace,
        string ClassName,
        string Namespace,
        IReadOnlyList<string> Commands,
        string? WindowPluginFactory = null,
        IReadOnlyList<string>? Platforms = null)
    {
        /// <summary>Platforms this plugin supports; unset means all (desktop, android, ios).</summary>
        public IReadOnlyList<string> EffectivePlatforms => Platforms ?? ["desktop", "android", "ios"];
    }
}
