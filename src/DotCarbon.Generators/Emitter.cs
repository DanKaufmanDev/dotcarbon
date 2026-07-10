using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DotCarbon.Generators;
internal sealed class Emitter
{
    private readonly SourceProductionContext _spc;
    private readonly string? _contextFq;
    private readonly StringBuilder _helpers = new();
    private readonly HashSet<string> _done = new();

    public Emitter(SourceProductionContext spc, string? contextFq)
    {
        _spc = spc;
        _contextFq = contextFq;
    }

    public string Helpers => _helpers.ToString();

    public string Read(ITypeSymbol type, string elem)
    {
        if (_contextFq is not null)
            return "(" + Fq(type) + ")global::System.Text.Json.JsonSerializer.Deserialize(" + elem +
                   ".GetRawText(), typeof(" + Fq(type) + "), " + _contextFq + ".Default)!";

        var core = Unwrap(type, out var nullable, out var isValue);
        var read = ReadCore(core, elem);
        if (!nullable) return read;
        var none = isValue ? "((" + Fq(core) + "?)null)" : "default(" + Fq(core) + ")";
        return "(" + elem + ".ValueKind == global::System.Text.Json.JsonValueKind.Null ? " + none + " : " + read + ")";
    }

    public string Write(ITypeSymbol type, string val)
    {
        if (_contextFq is not null)
            return "global::System.Text.Json.JsonSerializer.SerializeToNode(" + val + ", typeof(" + Fq(type) + "), " + _contextFq + ".Default)";

        var core = Unwrap(type, out var nullable, out var isValue);
        if (!nullable) return WriteCore(core, val);
        return isValue
            ? "(" + val + ".HasValue ? " + WriteCore(core, val + ".Value") + " : null)"
            : "(" + val + " is null ? null : " + WriteCore(core, val) + ")";
    }

    private string ReadCore(ITypeSymbol t, string e)
    {
        var prim = Primitive(t);
        if (prim is not null) return e + "." + prim + "()";
        if (t.TypeKind == TypeKind.Enum) return "(" + Fq(t) + ")" + e + ".GetInt32()";
        if (t is IArrayTypeSymbol arr) return EnsureArrayReader(arr.ElementType) + "(" + e + ")";
        if (TryList(t, out var le)) return EnsureListReader(t, le!) + "(" + e + ")";
        if (TryDict(t, out var ve)) return EnsureDictReader(ve!) + "(" + e + ")";
        if (t is INamedTypeSymbol nt && t.TypeKind is TypeKind.Class or TypeKind.Struct)
            return EnsureObjectReader(nt) + "(" + e + ")";
        Report(t); return "default!";
    }

    private string WriteCore(ITypeSymbol t, string v)
    {
        if (Primitive(t) is not null) return "global::System.Text.Json.Nodes.JsonValue.Create(" + v + ")";
        if (t.TypeKind == TypeKind.Enum) return "global::System.Text.Json.Nodes.JsonValue.Create((int)" + v + ")";
        if (t is IArrayTypeSymbol arr) return EnsureArrayWriter(arr.ElementType) + "(" + v + ")";
        if (TryList(t, out var le)) return EnsureListWriter(le!) + "(" + v + ")";
        if (TryDict(t, out var ve)) return EnsureDictWriter(ve!) + "(" + v + ")";
        if (t is INamedTypeSymbol nt && t.TypeKind is TypeKind.Class or TypeKind.Struct)
            return EnsureObjectWriter(nt) + "(" + v + ")";
        Report(t); return "null";
    }

    private string EnsureObjectReader(INamedTypeSymbol nt)
    {
        var name = "__R_" + Mangle(nt);
        if (!_done.Add(name)) return name;

        var ctor = nt.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0
                && !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, nt)))
            .OrderByDescending(c => c.Parameters.Length).FirstOrDefault();

        string body;
        if (ctor is not null)
        {
            var args = ctor.Parameters.Select((p, i) => ReadProp(p.Type, Camel(p.Name), i));
            body = "return new " + Fq(nt) + "(" + string.Join(", ", args) + ");";
        }
        else
        {
            var props = SettableProps(nt).ToList();
            var inits = props.Select((p, i) => p.Name + " = " + ReadProp(p.Type, Camel(p.Name), i));
            body = "return new " + Fq(nt) + " { " + string.Join(", ", inits) + " };";
        }
        Helper("private static " + Fq(nt) + " " + name + "(global::System.Text.Json.JsonElement __e)", body);
        return name;
    }

    private string EnsureObjectWriter(INamedTypeSymbol nt)
    {
        var name = "__W_" + Mangle(nt);
        if (!_done.Add(name)) return name;
        var props = WritableProps(nt)
            .Select(p => "[\"" + Camel(p.Name) + "\"] = " + Write(p.Type, "__v." + p.Name));
        var body = "if (__v is null) return null;\n        return new global::System.Text.Json.Nodes.JsonObject { " + string.Join(", ", props) + " };";
        Helper("private static global::System.Text.Json.Nodes.JsonNode? " + name + "(" + Fq(nt) + " __v)", body);
        return name;
    }

    private string EnsureArrayReader(ITypeSymbol el) => EnsureSeqReader(el, "__R_Arr_", Fq(el) + "[]", "__l.ToArray()");
    private string EnsureListReader(ITypeSymbol listT, ITypeSymbol el) => EnsureSeqReader(el, "__R_List_", Fq(listT), "__l");

    private string EnsureSeqReader(ITypeSymbol el, string prefix, string retType, string ret)
    {
        var name = prefix + Mangle(el);
        if (!_done.Add(name)) return name;
        var body = "var __l = new global::System.Collections.Generic.List<" + Fq(el) + ">();\n" +
                   "        foreach (var __x in __e.EnumerateArray()) __l.Add(" + Read(el, "__x") + ");\n" +
                   "        return " + ret + ";";
        Helper("private static " + retType + " " + name + "(global::System.Text.Json.JsonElement __e)", body);
        return name;
    }

    private string EnsureArrayWriter(ITypeSymbol el) => EnsureSeqWriter(el, "__W_Arr_", Fq(el) + "[]");
    private string EnsureListWriter(ITypeSymbol el) => EnsureSeqWriter(el, "__W_List_", "global::System.Collections.Generic.IEnumerable<" + Fq(el) + ">");

    private string EnsureSeqWriter(ITypeSymbol el, string prefix, string paramType)
    {
        var name = prefix + Mangle(el);
        if (!_done.Add(name)) return name;
        var body = "if (__v is null) return null;\n" +
                   "        var __a = new global::System.Text.Json.Nodes.JsonArray();\n" +
                   "        foreach (var __x in __v) __a.Add(" + Write(el, "__x") + ");\n" +
                   "        return __a;";
        Helper("private static global::System.Text.Json.Nodes.JsonNode? " + name + "(" + paramType + " __v)", body);
        return name;
    }

    private string EnsureDictReader(ITypeSymbol val)
    {
        var name = "__R_Dict_" + Mangle(val);
        if (!_done.Add(name)) return name;
        var t = "global::System.Collections.Generic.Dictionary<string, " + Fq(val) + ">";
        var body = "var __d = new " + t + "();\n" +
                   "        foreach (var __m in __e.EnumerateObject()) __d[__m.Name] = " + Read(val, "__m.Value") + ";\n" +
                   "        return __d;";
        Helper("private static " + t + " " + name + "(global::System.Text.Json.JsonElement __e)", body);
        return name;
    }

    private string EnsureDictWriter(ITypeSymbol val)
    {
        var name = "__W_Dict_" + Mangle(val);
        if (!_done.Add(name)) return name;
        var t = "global::System.Collections.Generic.IReadOnlyDictionary<string, " + Fq(val) + ">";
        var body = "if (__v is null) return null;\n" +
                   "        var __o = new global::System.Text.Json.Nodes.JsonObject();\n" +
                   "        foreach (var __m in __v) __o[__m.Key] = " + Write(val, "__m.Value") + ";\n" +
                   "        return __o;";
        Helper("private static global::System.Text.Json.Nodes.JsonNode? " + name + "(" + t + " __v)", body);
        return name;
    }

    private string ReadProp(ITypeSymbol type, string jsonName, int idx)
    {
        var v = "__f" + idx;
        return "(__e.TryGetProperty(\"" + jsonName + "\", out var " + v + ") && " + v +
               ".ValueKind != global::System.Text.Json.JsonValueKind.Null ? " + Read(type, v) + " : default(" + FqNull(type) + "))";
    }

    private void Helper(string signature, string body)
    {
        _helpers.Append("\n        ").Append(signature).Append("\n        {\n            ")
            .Append(body.Replace("\n        ", "\n            ")).Append("\n        }\n");
    }

    private void Report(ITypeSymbol t) => _spc.ReportDiagnostic(Diagnostic.Create(
        CarbonCommandGenerator.UnsupportedType, Location.None, t.ToDisplayString()));
        
    private static ITypeSymbol Unwrap(ITypeSymbol t, out bool nullable, out bool isValue)
    {
        if (t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n)
        {
            nullable = true; isValue = true; return n.TypeArguments[0];
        }
        if (t.IsReferenceType && t.NullableAnnotation == NullableAnnotation.Annotated)
        {
            nullable = true; isValue = false;
            return t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }
        nullable = false; isValue = false; return t;
    }

    private static string? Primitive(ITypeSymbol t) => t.SpecialType switch
    {
        SpecialType.System_String => "GetString",
        SpecialType.System_Boolean => "GetBoolean",
        SpecialType.System_Int32 => "GetInt32",
        SpecialType.System_Int64 => "GetInt64",
        SpecialType.System_Int16 => "GetInt16",
        SpecialType.System_Byte => "GetByte",
        SpecialType.System_Double => "GetDouble",
        SpecialType.System_Single => "GetSingle",
        SpecialType.System_Decimal => "GetDecimal",
        _ => null,
    };

    private static bool TryList(ITypeSymbol t, out ITypeSymbol? element)
    {
        element = null;
        if (t is not INamedTypeSymbol { IsGenericType: true } n) return false;
        var def = n.ConstructedFrom.ToDisplayString();
        if (def is "System.Collections.Generic.List<T>"
            or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.ICollection<T>"
            or "System.Collections.Generic.IEnumerable<T>")
        { element = n.TypeArguments[0]; return true; }
        return false;
    }

    private static bool TryDict(ITypeSymbol t, out ITypeSymbol? value)
    {
        value = null;
        if (t is not INamedTypeSymbol { IsGenericType: true } n) return false;
        var def = n.ConstructedFrom.ToDisplayString();
        if (def is "System.Collections.Generic.Dictionary<TKey, TValue>"
            or "System.Collections.Generic.IDictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
            && n.TypeArguments[0].SpecialType == SpecialType.System_String)
        { value = n.TypeArguments[1]; return true; }
        return false;
    }

    private static IEnumerable<IPropertySymbol> WritableProps(INamedTypeSymbol nt) =>
        nt.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public && p.GetMethod is not null);

    private static IEnumerable<IPropertySymbol> SettableProps(INamedTypeSymbol nt) =>
        WritableProps(nt).Where(p => p.SetMethod is not null);

    private static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static string Mangle(ITypeSymbol t) =>
        new string(Fq(t).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static string Fq(ITypeSymbol t) =>
        t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string FqNull(ITypeSymbol t)
    {
        var core = Unwrap(t, out var nullable, out _);
        return nullable ? Fq(core) + "?" : Fq(core);
    }
}
