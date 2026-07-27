using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotCarbon.Cli.Commands;

/// <summary>One plugin's public surface, parsed from its C# source.</summary>
internal sealed record PluginDoc(
    string Namespace,
    string Name,
    string? Description,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<CommandDoc> Commands,
    IReadOnlyList<PermissionDoc> Permissions);

internal sealed record CommandDoc(string FullName, string? Arguments, string? Result, string? Summary);

internal sealed record PermissionDoc(string Identifier, string? Description, IReadOnlyList<string> Commands);

/// <summary>
/// <c>carbon docs</c> — generates an API reference for every Carbon plugin by parsing its C# source
/// (the <c>[CarbonPlugin]</c>/<c>[CarbonCommand]</c>/<c>[CarbonPermission]</c> attributes and the XML
/// doc summaries). Generated from the code, so it cannot drift from the commands the app actually
/// exposes the way a hand-written page can. Runs against this repo to refresh the docs site, and works
/// on any Carbon project so plugin authors get the same reference for free.
/// </summary>
public static class DocsCommand
{
    public static Command Build()
    {
        var command = new Command("docs", "Generate a Markdown API reference from plugin sources");
        var project = new Option<DirectoryInfo?>(
            "--project", "Directory to scan for plugin sources (default: current directory)");
        var output = new Option<FileInfo?>(
            "--output", "Markdown file to write (default: docs/reference/commands.md)");
        var title = new Option<string>("--title", () => "Command reference", "Page title");
        command.AddOption(project);
        command.AddOption(output);
        command.AddOption(title);

        command.SetHandler(context =>
        {
            var root = context.ParseResult.GetValueForOption(project)?.FullName ?? Directory.GetCurrentDirectory();
            var outPath = context.ParseResult.GetValueForOption(output)?.FullName
                ?? Path.Combine(root, "docs", "reference", "commands.md");
            var pageTitle = context.ParseResult.GetValueForOption(title)!;

            var plugins = ScanDirectory(root);
            if (plugins.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Carbon] No [CarbonPlugin] classes found under {root}.");
                Console.ResetColor();
                context.ExitCode = 1;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, RenderMarkdown(plugins, pageTitle));

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Carbon] Wrote API reference for {plugins.Count} plugin(s) " +
                              $"({plugins.Sum(plugin => plugin.Commands.Count)} commands) -> " +
                              Path.GetRelativePath(root, outPath));
            Console.ResetColor();
        });

        return command;
    }

    /// <summary>Parses every <c>.cs</c> file under <paramref name="root"/>, sorted by namespace.</summary>
    internal static IReadOnlyList<PluginDoc> ScanDirectory(string root)
    {
        var plugins = new List<PluginDoc>();
        foreach (var file in EnumerateSources(root))
        {
            string source;
            try { source = File.ReadAllText(file); }
            catch (IOException) { continue; }
            if (!source.Contains("CarbonPlugin", StringComparison.Ordinal)) continue;
            plugins.AddRange(Parse(source));
        }

        return plugins
            .GroupBy(plugin => plugin.Namespace, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(plugin => plugin.Namespace, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> EnumerateSources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "node_modules"));

    /// <summary>Extracts every plugin declared in one source file (pure — the unit-testable core).</summary>
    internal static IReadOnlyList<PluginDoc> Parse(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var docs = new List<PluginDoc>();

        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var plugin = FindAttribute(cls.AttributeLists, "CarbonPlugin");
            if (plugin is null) continue;

            var ns = NamespaceOf(cls);
            if (ns is null) continue;

            var name = StringValue(Positional(plugin, 0)) ?? cls.Identifier.Text;
            var description = StringValue(Named(plugin, "description")) ?? StringValue(Positional(plugin, 2));

            var platformAttr = FindAttribute(cls.AttributeLists, "CarbonPluginPlatform");
            var platforms = platformAttr is null
                ? (IReadOnlyList<string>)["desktop", "android", "ios"]
                : platformAttr.ArgumentList?.Arguments
                    .Select(a => StringValue(a.Expression)).OfType<string>().ToList() ?? [];

            var permissions = AllAttributes(cls.AttributeLists, "CarbonPermission")
                .Select(attr => new PermissionDoc(
                    StringValue(Positional(attr, 0)) ?? string.Empty,
                    StringValue(Named(attr, "description")) ?? StringValue(Positional(attr, 1)),
                    StringArray(Named(attr, "Commands"))))
                .Where(permission => permission.Identifier.Length > 0)
                .ToList();

            var commands = new List<CommandDoc>();
            foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
            {
                var commandAttr = FindAttribute(method.AttributeLists, "CarbonCommand");
                if (commandAttr is null) continue;
                var commandName = StringValue(Positional(commandAttr, 0));
                if (commandName is null) continue;

                var argType = method.ParameterList.Parameters.Count > 0
                    ? method.ParameterList.Parameters[0].Type?.ToString()
                    : null;
                commands.Add(new CommandDoc(
                    $"{ns}:{commandName}",
                    argType,
                    UnwrapResult(method.ReturnType.ToString()),
                    SummaryOf(method)));
            }

            docs.Add(new PluginDoc(
                ns, name, description ?? SummaryOf(cls),
                platforms, commands, permissions));
        }

        return docs;
    }

    // ---- Markdown ------------------------------------------------------------------------------

    internal static string RenderMarkdown(IReadOnlyList<PluginDoc> plugins, string title)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {title}");
        builder.AppendLine("description: Every plugin command, generated from the source. Do not edit by hand.");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("<!-- Generated by `carbon docs`. Do not edit by hand. -->");
        builder.AppendLine();
        builder.AppendLine(
            $"{plugins.Count} plugins, {plugins.Sum(plugin => plugin.Commands.Count)} commands.");
        builder.AppendLine();

        foreach (var plugin in plugins)
        {
            builder.AppendLine($"## {plugin.Name}");
            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(plugin.Description))
                builder.AppendLine(plugin.Description).AppendLine();
            builder.AppendLine($"- **Namespace:** `{plugin.Namespace}`");
            builder.AppendLine($"- **Platforms:** {string.Join(", ", plugin.Platforms)}");
            builder.AppendLine();

            if (plugin.Commands.Count > 0)
            {
                builder.AppendLine("| Command | Arguments | Returns | Description |");
                builder.AppendLine("| --- | --- | --- | --- |");
                foreach (var command in plugin.Commands)
                    builder.AppendLine(
                        $"| `{command.FullName}` | {Code(command.Arguments)} | {Code(command.Result)} | " +
                        $"{Escape(command.Summary)} |");
                builder.AppendLine();
            }

            if (plugin.Permissions.Count > 0)
            {
                builder.AppendLine("**Permissions:**");
                builder.AppendLine();
                foreach (var permission in plugin.Permissions)
                {
                    var grants = permission.Commands.Count > 0
                        ? " — grants " + string.Join(", ", permission.Commands.Select(c => $"`{c}`"))
                        : string.Empty;
                    builder.AppendLine($"- `{permission.Identifier}`{grants}" +
                                       (string.IsNullOrWhiteSpace(permission.Description)
                                           ? string.Empty
                                           : $": {permission.Description}"));
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string Code(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : $"`{value}`";

    /// <summary>Table cells can't contain a raw pipe or newline.</summary>
    private static string Escape(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Replace("|", "\\|").Replace("\n", " ").Trim();

    // ---- Roslyn helpers ------------------------------------------------------------------------

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
        AllAttributes(lists, name).FirstOrDefault();

    private static IEnumerable<AttributeSyntax> AllAttributes(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(list => list.Attributes)
            .Where(attr => AttributeName(attr) == name);

    private static string AttributeName(AttributeSyntax attr)
    {
        var text = attr.Name.ToString();
        var lastDot = text.LastIndexOf('.');
        if (lastDot >= 0) text = text[(lastDot + 1)..];
        return text.EndsWith("Attribute", StringComparison.Ordinal) ? text[..^"Attribute".Length] : text;
    }

    /// <summary>The <paramref name="index"/>-th positional argument (no name: or name=).</summary>
    private static ExpressionSyntax? Positional(AttributeSyntax attr, int index)
    {
        var positional = attr.ArgumentList?.Arguments
            .Where(a => a.NameColon is null && a.NameEquals is null)
            .ToList();
        return positional is not null && index < positional.Count ? positional[index].Expression : null;
    }

    private static ExpressionSyntax? Named(AttributeSyntax attr, string name) =>
        attr.ArgumentList?.Arguments.FirstOrDefault(a =>
            a.NameColon?.Name.Identifier.Text == name || a.NameEquals?.Name.Identifier.Text == name)?.Expression;

    private static string? StringValue(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    private static IReadOnlyList<string> StringArray(ExpressionSyntax? expression) =>
        expression is null
            ? []
            : expression.DescendantNodesAndSelf()
                .OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText)
                .ToList();

    private static string? NamespaceOf(ClassDeclarationSyntax cls)
    {
        var property = cls.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.Text == "Namespace");
        // Almost always `public string Namespace => "clipboard";`.
        var value = property?.ExpressionBody?.Expression
            ?? property?.AccessorList?.Accessors
                .Select(a => a.ExpressionBody?.Expression).FirstOrDefault(e => e is not null);
        return StringValue(value);
    }

    /// <summary><c>Task&lt;T&gt;</c> → <c>T</c>; <c>Task</c>/<c>void</c> → <c>void</c>.</summary>
    private static string UnwrapResult(string returnType)
    {
        var trimmed = returnType.Trim();
        if (trimmed is "Task" or "void" or "ValueTask") return "void";
        var match = Regex.Match(trimmed, @"^(?:Task|ValueTask)<(.+)>$");
        return match.Success ? match.Groups[1].Value : trimmed;
    }

    private static string? SummaryOf(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia().ToFullString();
        var match = Regex.Match(trivia, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (!match.Success) return null;

        // Strip the `///` prefixes, any inline doc tags, and collapse whitespace.
        var text = Regex.Replace(match.Groups[1].Value, @"^\s*///?", string.Empty, RegexOptions.Multiline);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
