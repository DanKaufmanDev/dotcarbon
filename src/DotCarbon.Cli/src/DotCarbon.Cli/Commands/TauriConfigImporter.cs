using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCarbon.Cli.Commands;

/// <summary>What importing a tauri.conf.json produced.</summary>
internal sealed record TauriImport(
    JsonObject Carbon,
    string AppName,
    string Identifier,
    string? DevUrl,
    string TauriVersion,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Translates a Tauri config (v1 or v2) into a carbon.json object. Pure — it takes the config text,
/// not a path — so every field mapping and every "this doesn't translate" warning is unit-testable.
/// It maps what has a Carbon equivalent (app identity, window, dev/build wiring, CSP) and, crucially,
/// *reports what doesn't* (Rust commands, Tauri plugins, the allowlist/capability model) rather than
/// dropping it silently and leaving the user to discover the gap at runtime.
/// </summary>
internal static class TauriConfigImporter
{
    public static TauriImport Import(string tauriJson, string fallbackName)
    {
        JsonNode root = JsonNode.Parse(tauriJson, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? throw new JsonException("tauri.conf.json is empty.");

        // v1 nests almost everything under a `tauri` key and app identity under `package`; v2 hoists
        // identity to the top level and renames the build keys. Detect by structure.
        var isV1 = root["tauri"] is not null || root["package"] is not null;
        var version = isV1 ? "1" : "2";
        var warnings = new List<string>();

        var app = isV1 ? root["tauri"] : root["app"];
        var build = root["build"];
        var bundle = isV1 ? root["tauri"]?["bundle"] : root["bundle"];

        var name = Str(root["productName"]) ?? Str(root["package"]?["productName"])
            ?? Str(FirstWindow(app, isV1)?["title"]) ?? fallbackName;
        var appVersion = Str(root["version"]) ?? Str(root["package"]?["version"]) ?? "0.1.0";
        var identifier = Str(root["identifier"]) ?? Str(bundle?["identifier"])
            ?? $"com.example.{ProjectScaffold.SanitizeAppName(name).ToLowerInvariant()}";

        var (devUrl, devUrlWarning) = ReadDevUrl(build, isV1);
        if (devUrlWarning is not null) warnings.Add(devUrlWarning);

        var dist = ReadDist(build, isV1, warnings);
        var devCommand = Str(build?["beforeDevCommand"]);
        var buildCommand = Str(build?["beforeBuildCommand"]);

        var carbon = new JsonObject
        {
            ["configVersion"] = ConfigMigrationEngine.CurrentVersion,
            ["app"] = new JsonObject
            {
                ["name"] = name,
                ["version"] = appVersion,
                ["identifier"] = identifier,
            },
            ["window"] = Window(FirstWindow(app, isV1)),
            ["security"] = Security(app),
            ["build"] = Build(devCommand, buildCommand, devUrl, dist),
        };

        var category = Str(bundle?["category"]);
        if (category is not null)
            carbon["bundle"] = new JsonObject { ["category"] = category };

        CollectWarnings(root, app, bundle, isV1, warnings);
        return new TauriImport(carbon, name, identifier, devUrl, version, warnings);
    }

    private static JsonObject Window(JsonNode? window)
    {
        var result = new JsonObject();
        Copy(window, "title", result, "title", JsonKind.String);
        Copy(window, "width", result, "width", JsonKind.Number);
        Copy(window, "height", result, "height", JsonKind.Number);
        Copy(window, "minWidth", result, "minWidth", JsonKind.Number);
        Copy(window, "minHeight", result, "minHeight", JsonKind.Number);
        Copy(window, "maxWidth", result, "maxWidth", JsonKind.Number);
        Copy(window, "maxHeight", result, "maxHeight", JsonKind.Number);
        Copy(window, "resizable", result, "resizable", JsonKind.Bool);
        Copy(window, "fullscreen", result, "fullscreen", JsonKind.Bool);
        Copy(window, "maximized", result, "maximized", JsonKind.Bool);
        Copy(window, "alwaysOnTop", result, "alwaysOnTop", JsonKind.Bool);
        Copy(window, "decorations", result, "decorations", JsonKind.Bool);
        Copy(window, "transparent", result, "transparent", JsonKind.Bool);
        Copy(window, "center", result, "center", JsonKind.Bool);

        // The window has to belong to a capability or the bridge denies it every command.
        result["capabilities"] = new JsonArray("main");
        return result;
    }

    private static JsonObject Security(JsonNode? app)
    {
        var security = new JsonObject { ["enabled"] = true };
        // Tauri's csp is a string, an object (per-directive), or null; Carbon's is a single string.
        if (app?["security"]?["csp"] is JsonValue csp && csp.TryGetValue(out string? value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            security["csp"] = value;
        }

        return security;
    }

    private static JsonObject Build(string? devCommand, string? buildCommand, string? devUrl, string dist)
    {
        var build = new JsonObject();
        if (devCommand is not null) build["devCommand"] = devCommand;
        if (buildCommand is not null) build["buildCommand"] = buildCommand;
        if (devUrl is not null) build["devUrl"] = devUrl;
        build["frontendDist"] = dist;
        build["backendProject"] = ProjectScaffold.BackendDir;
        return build;
    }

    private static (string? DevUrl, string? Warning) ReadDevUrl(JsonNode? build, bool isV1)
    {
        // v2: build.devUrl. v1: build.devPath, which is a URL *or* a static directory.
        var raw = Str(build?["devUrl"]) ?? Str(build?["devPath"]);
        if (raw is null) return (null, null);
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return (raw, null);
        }

        // A non-URL devPath means Tauri served static files with no dev server; Carbon needs a URL.
        return (null, isV1
            ? $"build.devPath was '{raw}', not a URL — set build.devUrl to your dev server (e.g. http://localhost:5173)."
            : null);
    }

    private static string ReadDist(JsonNode? build, bool isV1, List<string> warnings)
    {
        var raw = Str(build?["frontendDist"]) ?? Str(build?["distDir"]) ?? "dist";

        // Tauri paths are relative to src-tauri/; Carbon's are relative to the project root. When
        // carbon.json is written at the root, the common "../dist" becomes "dist".
        var normalized = raw.Replace('\\', '/');
        if (normalized.StartsWith("../", StringComparison.Ordinal))
            normalized = normalized[3..];

        if (normalized.Contains("../", StringComparison.Ordinal))
            warnings.Add($"build.frontendDist is '{normalized}' — check it points at your build output " +
                         "relative to the project root.");

        return normalized.Length == 0 ? "dist" : normalized;
    }

    private static void CollectWarnings(
        JsonNode root, JsonNode? app, JsonNode? bundle, bool isV1, List<string> warnings)
    {
        // Rust commands are the biggest thing that does not translate: a Carbon backend is C#.
        warnings.Add("Rust `#[tauri::command]` handlers do not carry over — rewrite them as C# " +
                     "[CarbonCommand] methods in src-shared/AppCommands.cs.");

        if (root["plugins"] is JsonObject { Count: > 0 } plugins)
            warnings.Add($"Tauri plugins ({string.Join(", ", plugins.Select(entry => entry.Key))}) have no " +
                         "automatic equivalent — add Carbon plugins with `carbon add <name>`.");

        var hasAllowlist = isV1 && app?["allowlist"] is not null;
        var hasCapabilities = !isV1 && app?["security"]?["capabilities"] is not null;
        if (hasAllowlist || hasCapabilities)
            warnings.Add("Tauri's " + (isV1 ? "allowlist" : "capabilities") + " did not carry over — Carbon has " +
                         "its own capability model in src-carbon/capabilities/main.json.");

        if (bundle?["icon"] is not null)
            warnings.Add("Regenerate app icons from your source PNG with `carbon icon`.");
    }

    private static JsonNode? FirstWindow(JsonNode? app, bool isV1) =>
        (isV1 ? app?["windows"] : app?["windows"]) is JsonArray { Count: > 0 } windows ? windows[0] : null;

    private enum JsonKind { String, Number, Bool }

    /// <summary>Copies a value only when it is present and the right kind, so junk cannot leak through.</summary>
    private static void Copy(JsonNode? source, string sourceKey, JsonObject target, string targetKey, JsonKind kind)
    {
        if (source?[sourceKey] is not JsonValue value) return;

        switch (kind)
        {
            case JsonKind.String when value.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s):
                target[targetKey] = s;
                break;
            case JsonKind.Number when value.TryGetValue(out double d):
                // A ternary would unify both branches to double; assign separately so a whole number
                // is stored as an int (window sizes read back as ints, not 1024.0).
                if (d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue)
                    target[targetKey] = (int)d;
                else
                    target[targetKey] = d;
                break;
            case JsonKind.Bool when value.TryGetValue(out bool b):
                target[targetKey] = b;
                break;
        }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
