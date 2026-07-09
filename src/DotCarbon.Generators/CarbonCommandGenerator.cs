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
    private const string ContextBase = "System.Text.Json.Serialization.JsonSerializerContext";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "CARBON001", "Plugin class must be partial",
        "Class '{0}' has [CarbonCommand] methods and must be declared 'partial'",
        "DotCarbon", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor NoContext = new(
        "CARBON002", "Missing JsonSerializerContext",
        "Assembly has [CarbonCommand] methods but no JsonSerializerContext. Declare a 'partial class ... : JsonSerializerContext' with [JsonSerializable] for the command argument and return types.",
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
            .Where(static s => s is not null)
            .Collect();

        context.RegisterSourceOutput(methods.Combine(contexts),
            static (spc, pair) => Execute(spc, pair.Left, pair.Right!));
    }

    private static void Execute(SourceProductionContext spc, ImmutableArray<IMethodSymbol> methods, ImmutableArray<INamedTypeSymbol?> contexts)
    {
        if (methods.IsDefaultOrEmpty) return;

        var contextSymbol = contexts.FirstOrDefault(c => IsJsonContext(c!));
        if (contextSymbol is null)
        {
            spc.ReportDiagnostic(Diagnostic.Create(NoContext, methods[0].Locations.FirstOrDefault()));
            return;
        }
        var contextFq = Fq(contextSymbol);

        foreach (var group in methods.GroupBy<IMethodSymbol, INamedTypeSymbol>(
                     m => m.ContainingType, SymbolEqualityComparer.Default))
        {
            EmitForType(spc, group.Key, group.ToList(), contextFq);
        }
    }

    private static void EmitForType(SourceProductionContext spc, INamedTypeSymbol type, List<IMethodSymbol> commands, string contextFq)
    {
        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(t => t.Modifiers.Any(m => m.ValueText == "partial"));

        if (!isPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(NotPartial, type.Locations.FirstOrDefault(), type.Name));
            return;
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
        var reg = new StringBuilder();

        foreach (var method in commands)
        {
            var commandName = GetCommandName(method);
            if (commandName is null) continue;

            var hasArg = method.Parameters.Length == 1;
            var argType = hasArg ? Fq(method.Parameters[0].Type) : null;
            var (kind, innerType) = ClassifyReturn(method);

            var argDecl = hasArg
                ? $"                var __arg = ({argType})global::System.Text.Json.JsonSerializer.Deserialize(__p.GetRawText(), typeof({argType}), {contextFq}.Default)!;\n"
                : string.Empty;
            var callArgs = hasArg ? "__arg" : string.Empty;

            reg.Append($"            registry.Add(this.Namespace + \":{commandName}\", ");
            switch (kind)
            {
                case ReturnKind.SyncValue:
                    reg.Append("__p =>\n            {\n").Append(argDecl);
                    reg.Append($"                var __r = this.{method.Name}({callArgs});\n");
                    reg.Append($"                return global::System.Threading.Tasks.Task.FromResult<global::System.Text.Json.Nodes.JsonNode?>(global::System.Text.Json.JsonSerializer.SerializeToNode(__r, typeof({innerType}), {contextFq}.Default));\n");
                    break;
                case ReturnKind.TaskValue:
                    reg.Append("async __p =>\n            {\n").Append(argDecl);
                    reg.Append($"                var __r = await this.{method.Name}({callArgs});\n");
                    reg.Append($"                return global::System.Text.Json.JsonSerializer.SerializeToNode(__r, typeof({innerType}), {contextFq}.Default);\n");
                    break;
                case ReturnKind.Void:
                    reg.Append("__p =>\n            {\n").Append(argDecl);
                    reg.Append($"                this.{method.Name}({callArgs});\n");
                    reg.Append("                return global::System.Threading.Tasks.Task.FromResult<global::System.Text.Json.Nodes.JsonNode?>(null);\n");
                    break;
                case ReturnKind.Task:
                    reg.Append("async __p =>\n            {\n").Append(argDecl);
                    reg.Append($"                await this.{method.Name}({callArgs});\n");
                    reg.Append("                return null;\n");
                    break;
            }
            reg.Append("            });\n");
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (ns is not null) sb.AppendLine($"namespace {ns}\n{{");
        sb.AppendLine($"    partial class {type.Name}");
        sb.AppendLine("    {");
        sb.AppendLine("        public void Register(global::DotCarbon.Core.Bridge.ICommandRegistry registry)");
        sb.AppendLine("        {");
        sb.Append(reg);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        if (ns is not null) sb.AppendLine("}");

        var hint = (ns is null ? "" : ns + ".") + type.Name + ".Carbon.g.cs";
        spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool IsJsonContext(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == ContextBase)
                return true;
        return false;
    }

    private static string? GetCommandName(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeName);
        if (attr is null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    private enum ReturnKind { Void, SyncValue, Task, TaskValue }

    private static (ReturnKind, string?) ClassifyReturn(IMethodSymbol method)
    {
        if (method.ReturnsVoid) return (ReturnKind.Void, null);

        var rt = method.ReturnType;
        var def = rt.OriginalDefinition.ToDisplayString();

        if (def == "System.Threading.Tasks.Task") return (ReturnKind.Task, null);
        if (def == "System.Threading.Tasks.Task<TResult>" && rt is INamedTypeSymbol named)
            return (ReturnKind.TaskValue, Fq(named.TypeArguments[0]));
        return (ReturnKind.SyncValue, Fq(rt));
    }

    private static string Fq(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
