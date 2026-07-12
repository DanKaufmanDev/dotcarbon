using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotCarbon.Generators;

[Generator]
public sealed class CarbonCommandGenerator : IIncrementalGenerator
{
    private const string AttributeName = "DotCarbon.Core.Bridge.CarbonCommandAttribute";
    private const string PluginAttributeName = "DotCarbon.Core.Plugins.CarbonPluginAttribute";
    private const string PermissionAttributeName = "DotCarbon.Core.Plugins.CarbonPermissionAttribute";
    private const string EventAttributeName = "DotCarbon.Core.Plugins.CarbonEventAttribute";
    private const string PlatformAttributeName = "DotCarbon.Core.Plugins.CarbonPluginPlatformAttribute";
    private const string ContextBase = "System.Text.Json.Serialization.JsonSerializerContext";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "CARBON001", "Command class must be partial",
        "Class '{0}' has [CarbonCommand] methods and must be declared 'partial'",
        "DotCarbon", DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor UnsupportedType = new(
        "CARBON003", "Unsupported command type",
        "Type '{0}' can't be auto-serialized by DotCarbon. Use primitives, records/classes, arrays, List<T>, Dictionary<string,T>, enums, or add a JsonSerializerContext.",
        "DotCarbon", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol).Collect();

        var contexts = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is ClassDeclarationSyntax c && c.BaseList is not null
                && c.BaseList.Types.Any(t => t.Type.ToString().Contains("JsonSerializerContext")),
            transform: static (ctx, _) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol)
            .Where(static s => s is not null).Collect();

        context.RegisterSourceOutput(methods.Combine(contexts),
            static (spc, pair) => Execute(spc, pair.Left, pair.Right!));
    }

    private static void Execute(SourceProductionContext spc, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol?> contexts)
    {
        if (methods.IsDefaultOrEmpty) return;

        // If the assembly ships a JsonSerializerContext, route serialization through it
        // (keeps existing plugin packages working). Otherwise generate it inline — zero boilerplate.
        var context = contexts.FirstOrDefault(c => IsJsonContext(c!));
        var contextFq = context is null ? null : Fq(context);

        foreach (var group in methods.GroupBy<IMethodSymbol, INamedTypeSymbol>(
                     m => m.ContainingType, SymbolEqualityComparer.Default))
        {
            EmitForType(spc, group.Key, group.ToList(), contextFq);
        }
    }

    private static void EmitForType(SourceProductionContext spc, INamedTypeSymbol type, List<IMethodSymbol> commands, string? contextFq)
    {
        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>()
            .Any(t => t.Modifiers.Any(m => m.ValueText == "partial"));
        if (!isPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(NotPartial, type.Locations.FirstOrDefault(), type.Name));
            return;
        }

        var em = new Emitter(spc, contextFq);
        var reg = new StringBuilder();
        var metadataCommands = new StringBuilder();

        foreach (var method in commands)
        {
            var name = GetCommandName(method);
            if (name is null) continue;

            var hasArg = method.Parameters.Length == 1;
            var argType = hasArg
                ? method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
            var argDecl = hasArg
                ? "                var __arg = " + em.Read(method.Parameters[0].Type, "__p") + ";\n"
                : string.Empty;
            var call = "this." + method.Name + "(" + (hasArg ? "__arg" : "") + ")";
            var (kind, inner) = ClassifyReturn(method);
            var resultType = inner?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ?? (kind == ReturnKind.Task || kind == ReturnKind.Void
                    ? "void"
                    : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            reg.Append("            registry.Add(this.Namespace + \":" + name + "\", ");
            metadataCommands.Append("                new global::DotCarbon.Core.Plugins.PluginCommandMetadata(")
                .Append(Literal(name)).Append(", this.Namespace + \":").Append(name).Append("\", ")
                .Append(LiteralOrNull(argType)).Append(", ")
                .Append(Literal(resultType)).AppendLine("),");
            switch (kind)
            {
                case ReturnKind.SyncValue:
                    reg.Append("__p =>\n            {\n").Append(argDecl);
                    reg.Append("                var __r = " + call + ";\n");
                    reg.Append("                return global::System.Threading.Tasks.Task.FromResult<global::System.Text.Json.Nodes.JsonNode?>(" + em.Write(inner!, "__r") + ");\n");
                    break;
                case ReturnKind.TaskValue:
                    reg.Append("async __p =>\n            {\n").Append(argDecl);
                    reg.Append("                var __r = await " + call + ";\n");
                    reg.Append("                return " + em.Write(inner!, "__r") + ";\n");
                    break;
                case ReturnKind.Void:
                    reg.Append("__p =>\n            {\n").Append(argDecl);
                    reg.Append("                " + call + ";\n");
                    reg.Append("                return global::System.Threading.Tasks.Task.FromResult<global::System.Text.Json.Nodes.JsonNode?>(null);\n");
                    break;
                case ReturnKind.Task:
                    reg.Append("async __p =>\n            {\n").Append(argDecl);
                    reg.Append("                await " + call + ";\n");
                    reg.Append("                return null;\n");
                    break;
            }
            reg.Append("            });\n");
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8619");
        sb.AppendLine();
        if (ns is not null) sb.AppendLine("namespace " + ns + "\n{");
        sb.AppendLine("    partial class " + type.Name);
        sb.AppendLine("    {");
        sb.AppendLine("        public void Register(global::DotCarbon.Core.Bridge.ICommandRegistry registry)");
        sb.AppendLine("        {");
        sb.Append(reg);
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.Append(EmitMetadata(type, metadataCommands.ToString()));
        sb.Append(em.Helpers);
        sb.AppendLine("    }");
        if (ns is not null) sb.AppendLine("}");

        var hint = (ns is null ? "" : ns + ".") + type.Name + ".Carbon.g.cs";
        spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EmitMetadata(INamedTypeSymbol type, string commands)
    {
        var attr = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PluginAttributeName);
        var name = GetConstructorString(attr, 0) ?? type.Name;
        var version = GetConstructorString(attr, 1);
        var description = GetConstructorString(attr, 2);
        var platforms = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PlatformAttributeName);
        IReadOnlyList<string> platformValues = platforms is null ? [] : GetConstructorStringArray(platforms, 0);

        var permissions = new StringBuilder();
        foreach (var permission in type.GetAttributes()
                     .Where(a => a.AttributeClass?.ToDisplayString() == PermissionAttributeName))
        {
            var identifier = GetConstructorString(permission, 0);
            if (identifier is null) continue;
            var permissionDescription = GetConstructorString(permission, 1);
            var commandValues = GetNamedStringArray(permission, "Commands");

            permissions.Append("                new global::DotCarbon.Core.Plugins.PluginPermissionMetadata(")
                .Append(Literal(identifier)).Append(", ")
                .Append(LiteralOrNull(permissionDescription)).Append(", ")
                .Append(StringArray(commandValues)).AppendLine("),");
        }

        var events = new StringBuilder();
        foreach (var evt in type.GetAttributes()
                     .Where(a => a.AttributeClass?.ToDisplayString() == EventAttributeName))
        {
            var eventName = GetConstructorString(evt, 0);
            if (eventName is null) continue;
            var payloadType = GetConstructorString(evt, 1);
            var eventDescription = GetConstructorString(evt, 2);

            events.Append("                new global::DotCarbon.Core.Plugins.PluginEventMetadata(")
                .Append(Literal(eventName)).Append(", ")
                .Append(LiteralOrNull(payloadType)).Append(", ")
                .Append(LiteralOrNull(eventDescription)).AppendLine("),");
        }

        return
            "        public global::DotCarbon.Core.Plugins.PluginMetadata Metadata =>\n" +
            "            new global::DotCarbon.Core.Plugins.PluginMetadata(\n" +
            "                this.Namespace,\n" +
            "                " + Literal(name) + ",\n" +
            "                " + LiteralOrNull(version) + ",\n" +
            "                " + LiteralOrNull(description) + ",\n" +
            "                new global::DotCarbon.Core.Plugins.PluginCommandMetadata[]\n" +
            "                {\n" +
            commands +
            "                },\n" +
            "                new global::DotCarbon.Core.Plugins.PluginPermissionMetadata[]\n" +
            "                {\n" +
            permissions +
            "                },\n" +
            "                new global::DotCarbon.Core.Plugins.PluginEventMetadata[]\n" +
            "                {\n" +
            events +
            "                },\n" +
            "                " + (platforms is null ? "null" : StringArray(platformValues)) + ");\n\n";
    }

    private static bool IsJsonContext(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == ContextBase) return true;
        return false;
    }

    private static string? GetCommandName(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeName);
        return attr is null || attr.ConstructorArguments.Length == 0 ? null : attr.ConstructorArguments[0].Value as string;
    }

    private static string? GetConstructorString(AttributeData? attr, int index)
    {
        if (attr is null || attr.ConstructorArguments.Length <= index) return null;
        return attr.ConstructorArguments[index].Value as string;
    }

    private static IReadOnlyList<string> GetNamedStringArray(AttributeData attr, string name)
    {
        foreach (var argument in attr.NamedArguments)
        {
            if (argument.Key != name) continue;
            return argument.Value.Values
                .Select(value => value.Value as string)
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
        }
        return [];
    }

    private static IReadOnlyList<string> GetConstructorStringArray(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return [];
        return attr.ConstructorArguments[index].Values
            .Select(value => value.Value as string)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
    }

    private static string LiteralOrNull(string? value) =>
        value is null ? "null" : Literal(value);

    private static string Literal(string value) =>
        "@\"" + value.Replace("\"", "\"\"") + "\"";

    private static string StringArray(IReadOnlyList<string> values) =>
        "new string[] { " + string.Join(", ", values.Select(Literal)) + " }";

    private enum ReturnKind { Void, SyncValue, Task, TaskValue }

    private static (ReturnKind, ITypeSymbol?) ClassifyReturn(IMethodSymbol method)
    {
        if (method.ReturnsVoid) return (ReturnKind.Void, null);
        var rt = method.ReturnType;
        var def = rt.OriginalDefinition.ToDisplayString();
        if (def == "System.Threading.Tasks.Task") return (ReturnKind.Task, null);
        if (def == "System.Threading.Tasks.Task<TResult>" && rt is INamedTypeSymbol n)
            return (ReturnKind.TaskValue, n.TypeArguments[0]);
        return (ReturnKind.SyncValue, rt);
    }

    private static string Fq(ITypeSymbol t) =>
        t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
