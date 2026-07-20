using System.CommandLine;
using System.Text.Json.Nodes;

namespace DotCarbon.Cli.Commands;

/// <summary>
/// <c>carbon permission new/ls</c> — scaffold and list plugin permissions. <c>new</c> writes an entry
/// into a plugin permission manifest (the file `carbon capabilities` discovers, Task 5.5); <c>ls</c>
/// lists first-party and discovered permissions.
/// </summary>
public static class PermissionCommand
{
    public static Command Build()
    {
        var command = new Command("permission", "Scaffold and list plugin permissions");
        command.AddCommand(NewSubcommand());
        command.AddCommand(ListSubcommand());
        return command;
    }

    private static Command NewSubcommand()
    {
        var cmd = new Command("new", "Add a permission to a plugin permission manifest");
        var identifier = new Argument<string>("identifier", "Permission id, e.g. myplugin:allow-sync");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var commands = new Option<string[]>(
            "--commands", "Command patterns this permission grants (default: derived from the id)")
        { AllowMultipleArgumentsPerToken = true };
        var description = new Option<string?>("--description", "Human-readable description");
        cmd.AddArgument(identifier);
        cmd.AddOption(project);
        cmd.AddOption(commands);
        cmd.AddOption(description);
        cmd.SetHandler((id, projectDir, grantedCommands, desc) =>
        {
            var root = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            if (!TrySplitNamespace(id, out var ns))
            {
                CapabilitiesCommand.WriteColor(
                    $"[Carbon] Invalid permission id '{id}'. Expected <namespace>:<name>.", ConsoleColor.Red);
                return;
            }

            var path = Path.Combine(root, "src-carbon", "permissions", ns + ".json");
            var manifest = LoadOrCreateManifest(path, ns);
            var permissions = CapabilitiesCommand.GetArray(manifest, "permissions");
            if (permissions.OfType<JsonObject>().Any(entry => entry["identifier"]?.GetValue<string>() == id))
            {
                CapabilitiesCommand.WriteColor(
                    $"[Carbon] {id} is already present in {Path.GetRelativePath(root, path)}", ConsoleColor.Yellow);
                return;
            }

            var entryCommands = grantedCommands.Length > 0 ? grantedCommands : [DeriveCommand(id, ns)];
            var permission = new JsonObject { ["identifier"] = id };
            if (!string.IsNullOrWhiteSpace(desc)) permission["description"] = desc;
            var commandArray = new JsonArray();
            foreach (var pattern in entryCommands) commandArray.Add(pattern);
            permission["commands"] = commandArray;
            permissions.Add(permission);

            CapabilitiesCommand.SaveJson(manifest, path);
            CapabilitiesCommand.WriteColor(
                $"[Carbon] Added permission {id} -> {Path.GetRelativePath(root, path)}", ConsoleColor.Green);
        }, identifier, project, commands, description);
        return cmd;
    }

    private static Command ListSubcommand()
    {
        var cmd = new Command("ls", "List known permissions (first-party + discovered plugin manifests)");
        var project = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        cmd.AddOption(project);
        cmd.SetHandler(projectDir =>
        {
            var root = projectDir?.FullName ?? Directory.GetCurrentDirectory();
            foreach (var permission in CapabilityPermissionCatalog.ForProject(root))
                Console.WriteLine(
                    $"{permission.Id}  ({permission.PluginNamespace})  {string.Join(", ", permission.Commands)}");
        }, project);
        return cmd;
    }

    private static JsonObject LoadOrCreateManifest(string path, string ns)
    {
        if (File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing) return existing;
            }
            catch (System.Text.Json.JsonException) { /* fall through to a fresh manifest */ }
        }
        return new JsonObject { ["namespace"] = ns, ["permissions"] = new JsonArray() };
    }

    private static bool TrySplitNamespace(string id, out string ns)
    {
        ns = string.Empty;
        var colon = id.IndexOf(':');
        if (colon <= 0 || colon == id.Length - 1 || id.IndexOf(':', colon + 1) >= 0) return false;
        ns = id[..colon];
        return true;
    }

    private static string DeriveCommand(string id, string ns)
    {
        // "myplugin:allow-read-file" grants "myplugin:read_file"; anything else grants the whole namespace.
        var local = id[(ns.Length + 1)..];
        return local.StartsWith("allow-", StringComparison.Ordinal)
            ? $"{ns}:{local["allow-".Length..].Replace('-', '_')}"
            : $"{ns}:*";
    }
}
