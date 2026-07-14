using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotCarbon.Cli.Commands;

public static class TypesCommand
{
    public static Command Build()
    {
        var command = new Command("types", "Generate carbon.d.ts from your [CarbonCommand] methods");

        var projectOption = new Option<DirectoryInfo?>(
            "--project", "Path to the Carbon project (default: current directory)");
        var outOption = new Option<FileInfo?>(
            "--out", "Output path (default: ui/src/carbon.d.ts)");
        var noCapabilitiesOption = new Option<bool>(
            "--no-capabilities", "Do not sync discovered commands into src-carbon/capabilities/main.json");

        command.AddOption(projectOption);
        command.AddOption(outOption);
        command.AddOption(noCapabilitiesOption);
        command.SetHandler(Run, projectOption, outOption, noCapabilitiesOption);
        return command;
    }

    private static void Run(DirectoryInfo? projectDir, FileInfo? outFile, bool noCapabilities)
    {
        var root = projectDir?.FullName ?? Directory.GetCurrentDirectory();
        var result = Generate(root, outFile?.FullName, !noCapabilities);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"⚡ Generated {result.CommandCount} command type(s) → {result.TargetPath}");
        Console.ResetColor();
        if (result.SyncedCapabilityCount > 0 && result.CapabilityPath is not null)
            Console.WriteLine($"⚡ Synced {result.SyncedCapabilityCount} command(s) → {result.CapabilityPath}");
    }

    internal static TypesGenerationResult Generate(
        string root,
        string? outPath = null,
        bool syncCapabilities = true)
    {
        // Commands can live in the desktop host (src-carbon) and/or the shared backend (src-shared).
        var searchDirs = new[] { "src-carbon", "src-shared" }
            .Select(dir => Path.Combine(root, dir))
            .Where(Directory.Exists)
            .ToList();
        if (searchDirs.Count == 0) searchDirs.Add(root);

        var files = searchDirs
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Distinct()
            .ToList();

        var roots = files
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetCompilationUnitRoot())
            .ToList();

        var records = new Dictionary<string, List<(string Name, string Type)>>();
        foreach (var r in roots.SelectMany(r => r.DescendantNodes().OfType<RecordDeclarationSyntax>()))
        {
            var props = r.ParameterList?.Parameters
                .Select(p => (p.Identifier.Text, p.Type!.ToString()))
                .ToList() ?? new List<(string, string)>();
            records[r.Identifier.Text] = props;
        }

        var commands = new List<CarbonCommandInfo>();
        var plugins = new Dictionary<string, string>();

        foreach (var cls in roots.SelectMany(r => r.DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            var implementsPlugin = cls.BaseList?.Types.Any(t => t.Type.ToString() == "IPlugin") ?? false;
            if (!implementsPlugin) continue;

            var ns = ExtractNamespace(cls);
            if (ns is null) continue;
            plugins[ns] = cls.Identifier.Text;

            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var attr = method.AttributeLists.SelectMany(a => a.Attributes)
                    .FirstOrDefault(a => a.Name.ToString() is "CarbonCommand" or "CarbonCommandAttribute");
                if (attr is null) continue;

                var nameArg = attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression as LiteralExpressionSyntax;
                if (nameArg is null) continue;
                var localName = nameArg.Token.ValueText;
                var cmd = $"{ns}:{localName}";

                var argType = method.ParameterList.Parameters.Count == 1
                    ? method.ParameterList.Parameters[0].Type!.ToString()
                    : null;

                commands.Add(new CarbonCommandInfo(ns, localName, cmd, argType, method.ReturnType.ToString()));
            }
        }

        var dts = Emit(commands.OrderBy(c => c.Name).ToList(), plugins, records);
        var target = outPath ?? Path.Combine(root, "ui", "src", "carbon.d.ts");
        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);
        File.WriteAllText(target, dts);

        var syncResult = syncCapabilities
            ? SyncMainCapability(root, commands)
            : new CapabilitySyncResult(0, null);

        return new TypesGenerationResult(
            commands.Count,
            target,
            syncResult.AddedCommandCount,
            syncResult.CapabilityPath);
    }

    private static string? ExtractNamespace(ClassDeclarationSyntax cls)
    {
        var prop = cls.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.Text == "Namespace");
        var literal = prop?.ExpressionBody?.Expression as LiteralExpressionSyntax
            ?? prop?.AccessorList?.Accessors
                .Select(a => a.ExpressionBody?.Expression as LiteralExpressionSyntax)
                .FirstOrDefault(e => e is not null);
        return literal?.Token.ValueText;
    }

    private static string Emit(
        List<CarbonCommandInfo> commands,
        Dictionary<string, string> plugins,
        Dictionary<string, List<(string Name, string Type)>> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// AUTO-GENERATED by `carbon types` — do not edit.");
        sb.AppendLine("import '@dotcarbon/api';");
        sb.AppendLine();
        sb.AppendLine("declare module '@dotcarbon/api' {");
        sb.AppendLine("    interface CarbonCommands {");
        foreach (var (_, _, name, argType, returnType) in commands)
        {
            var args = argType is null ? "void" : TsType(argType, records);
            var result = TsType(returnType, records);
            sb.AppendLine($"        '{name}': {{ args: {args}; result: {result} }};");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export declare const carbonGeneratedPluginMetadata: CarbonGeneratedPluginMetadata;");
        sb.AppendLine();
        sb.AppendLine("export type CarbonGeneratedPluginMetadata = readonly [");
        foreach (var group in commands.GroupBy(command => command.Namespace).OrderBy(group => group.Key))
        {
            var pluginName = plugins.GetValueOrDefault(group.Key, group.Key);
            sb.AppendLine("    {");
            sb.AppendLine($"        readonly namespace: '{group.Key}';");
            sb.AppendLine($"        readonly name: '{pluginName}';");
            sb.AppendLine("        readonly version: null;");
            sb.AppendLine("        readonly description: null;");
            sb.AppendLine("        readonly commands: readonly [");
            foreach (var command in group.OrderBy(command => command.Name))
            {
                sb.AppendLine("            {");
                sb.AppendLine($"                readonly name: '{command.LocalName}';");
                sb.AppendLine($"                readonly fullName: '{command.Name}';");
                sb.AppendLine($"                readonly arguments: {StringLiteralType(command.ArgType)};");
                sb.AppendLine($"                readonly result: {StringLiteralType(command.ReturnType)};");
                sb.AppendLine("            },");
            }
            sb.AppendLine("        ];");
            sb.AppendLine("        readonly permissions: readonly [];");
            sb.AppendLine("        readonly events: readonly [];");
            sb.AppendLine("    },");
        }
        sb.AppendLine("];");
        return sb.ToString();
    }

    private static CapabilitySyncResult SyncMainCapability(string root, IReadOnlyCollection<CarbonCommandInfo> commands)
    {
        if (commands.Count == 0)
            return new CapabilitySyncResult(0, null);

        var carbonDir = Path.Combine(root, "src-carbon");
        if (!Directory.Exists(carbonDir))
            return new CapabilitySyncResult(0, null);

        var capabilityDir = Path.Combine(carbonDir, "capabilities");
        Directory.CreateDirectory(capabilityDir);

        var capabilityPath = Path.Combine(capabilityDir, "main.json");
        var document = LoadOrCreateMainCapability(capabilityPath);
        var commandsNode = document["commands"] as JsonArray;
        if (commandsNode is null)
        {
            commandsNode = [];
            document["commands"] = commandsNode;
        }

        var existing = commandsNode
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var command in commands.OrderBy(command => command.Name))
        {
            if (existing.Contains(command.Name) || IsCoveredByWildcard(existing, command.Name))
                continue;

            commandsNode.Add(command.Name);
            existing.Add(command.Name);
            added++;
        }

        if (added > 0 || !File.Exists(capabilityPath))
        {
            var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(capabilityPath, json + Environment.NewLine);
        }

        return new CapabilitySyncResult(added, capabilityPath);
    }

    private static JsonObject LoadOrCreateMainCapability(string capabilityPath)
    {
        if (File.Exists(capabilityPath))
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(capabilityPath)) as JsonObject
                    ?? CreateMainCapability();
            }
            catch (JsonException)
            {
                return CreateMainCapability();
            }
        }

        return CreateMainCapability();
    }

    private static JsonObject CreateMainCapability() => new()
    {
        ["description"] = "Main window permissions.",
        ["windows"] = new JsonArray("main"),
        ["commands"] = new JsonArray()
    };

    private static bool IsCoveredByWildcard(HashSet<string> existing, string commandName)
    {
        var separator = commandName.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0) return false;

        var namespaceWildcard = commandName[..separator] + ":*";
        return existing.Contains(namespaceWildcard) || existing.Contains("*");
    }

    private static string StringLiteralType(string? value) =>
        value is null ? "null" : $"'{value.Replace("'", "\\'")}'";

    private static string TsType(string csType, Dictionary<string, List<(string Name, string Type)>> records)
    {
        csType = csType.Trim();

        if (csType == "Task" || csType == "void") return "void";
        if (csType.StartsWith("Task<") && csType.EndsWith(">"))
            return TsType(csType[5..^1], records);

        var nullable = csType.EndsWith("?");
        if (nullable) csType = csType[..^1].Trim();

        string ts;
        if (csType.EndsWith("[]"))
            ts = TsType(csType[..^2], records) + "[]";
        else
            ts = csType switch
            {
                "string" => "string",
                "bool" => "boolean",
                "int" or "long" or "short" or "byte" or "double" or "float" or "decimal" => "number",
                _ when records.TryGetValue(csType, out var props) =>
                    "{ " + string.Join("; ", props.Select(p => $"{Camel(p.Name)}: {TsType(p.Type, records)}")) + " }",
                _ => "unknown",
            };

        return nullable ? $"{ts} | null" : ts;
    }

    private static string Camel(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

internal readonly record struct CarbonCommandInfo(
    string Namespace,
    string LocalName,
    string Name,
    string? ArgType,
    string ReturnType);

internal readonly record struct TypesGenerationResult(
    int CommandCount,
    string TargetPath,
    int SyncedCapabilityCount,
    string? CapabilityPath);

internal readonly record struct CapabilitySyncResult(int AddedCommandCount, string? CapabilityPath);
