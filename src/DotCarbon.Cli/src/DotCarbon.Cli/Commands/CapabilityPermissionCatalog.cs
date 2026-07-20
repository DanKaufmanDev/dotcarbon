using System.Text.Json;
using System.Text.Json.Serialization;
using DotCarbon.Core.Config;

namespace DotCarbon.Cli.Commands;

internal sealed record CapabilityPermissionDefinition(
    string Id,
    string PluginNamespace,
    string Description,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<CapabilityRequirement> Requirements);

internal sealed record CapabilityRequirement(string Path, string Hint);

// A permission manifest a third-party plugin ships (dropped under <project>/(src-carbon/)permissions/*.json),
// so `carbon capabilities` discovers its permissions and scope requirements without a hardcoded catalog entry.
internal sealed record PermissionManifest(
    string? Namespace = null,
    string? Package = null,
    string[]? Platforms = null,
    PermissionManifestEntry[]? Permissions = null);

internal sealed record PermissionManifestEntry(
    string Identifier,
    string? Description = null,
    string[]? Commands = null,
    PermissionManifestRequirement[]? Requirements = null);

internal sealed record PermissionManifestRequirement(string Path, string Hint);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PermissionManifest))]
internal partial class PermissionManifestJsonContext : JsonSerializerContext;

internal static class CapabilityPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, CapabilityRequirement[]> RequirementsByNamespace =
        new Dictionary<string, CapabilityRequirement[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["fs"] =
            [
                new("plugins.fs.scopes", "set plugins.fs.scopes to the smallest app-owned directories the frontend needs")
            ],
            ["http"] =
            [
                new("plugins.http.scope", "set plugins.http.scope to exact origins/path prefixes instead of relying on dev allow-all")
            ],
            ["shell"] =
            [
                new("plugins.shell.allowedPrograms", "set plugins.shell.allowedPrograms to exact executable names or paths")
            ],
            ["updater"] =
            [
                new("bundle.updater.endpoints", "set bundle.updater.endpoints to signed update manifest URLs"),
                new("bundle.updater.publicKey", "set bundle.updater.publicKey to the update signing public key")
            ]
        };

    public static IReadOnlyList<CapabilityPermissionDefinition> All =>
        AddCommand.Catalog.Values
            .Select(definition =>
            {
                var id = definition.Namespace + ":default";
                var requirements = RequirementsByNamespace.TryGetValue(definition.Namespace, out var value)
                    ? value
                    : [];
                return new CapabilityPermissionDefinition(
                    id,
                    definition.Namespace,
                    $"{definition.NuGetPackage} default permission.",
                    definition.Commands,
                    definition.EffectivePlatforms,
                    requirements);
            })
            .OrderBy(permission => permission.Id, StringComparer.Ordinal)
            .ToArray();

    private static readonly string[] AllPlatforms = ["desktop", "android", "ios"];

    /// <summary>
    /// Discovers third-party permission manifests dropped under <c>src-carbon/permissions/*.json</c> or
    /// <c>permissions/*.json</c> — a package can ship one so its permissions show up here without a
    /// hardcoded catalog entry.
    /// </summary>
    public static IReadOnlyList<CapabilityPermissionDefinition> Discover(string projectDir)
    {
        var directories = new[]
        {
            Path.Combine(projectDir, "src-carbon", "permissions"),
            Path.Combine(projectDir, "permissions"),
        };

        var definitions = new List<CapabilityPermissionDefinition>();
        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                PermissionManifest? manifest;
                try
                {
                    manifest = JsonSerializer.Deserialize(
                        File.ReadAllText(file), PermissionManifestJsonContext.Default.PermissionManifest);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (manifest?.Permissions is null || string.IsNullOrWhiteSpace(manifest.Namespace))
                    continue;

                foreach (var entry in manifest.Permissions)
                {
                    if (string.IsNullOrWhiteSpace(entry.Identifier)) continue;
                    definitions.Add(new CapabilityPermissionDefinition(
                        entry.Identifier,
                        manifest.Namespace!,
                        entry.Description ?? $"{manifest.Package ?? manifest.Namespace} permission.",
                        entry.Commands ?? [],
                        manifest.Platforms ?? AllPlatforms,
                        (entry.Requirements ?? [])
                            .Select(requirement => new CapabilityRequirement(requirement.Path, requirement.Hint))
                            .ToArray()));
                }
            }
        }

        return definitions;
    }

    /// <summary>The first-party catalog plus any discovered third-party manifests; first-party wins on id.</summary>
    public static IReadOnlyList<CapabilityPermissionDefinition> ForProject(string projectDir)
    {
        var merged = All.ToList();
        var known = merged.Select(definition => definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var discovered in Discover(projectDir))
            if (known.Add(discovered.Id))
                merged.Add(discovered);

        return merged.OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
    }

    public static CapabilityPermissionDefinition? Resolve(string value) => Resolve(All, value);

    public static CapabilityPermissionDefinition? Resolve(
        IReadOnlyList<CapabilityPermissionDefinition> catalog, string value)
    {
        // An exact id or the namespace's default wins over a bare-namespace alias, which now that a
        // namespace can expose several permissions would otherwise be ambiguous.
        return catalog.FirstOrDefault(permission => permission.Id.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? catalog.FirstOrDefault(permission => permission.Id.Equals(value + ":default", StringComparison.OrdinalIgnoreCase))
            ?? catalog.FirstOrDefault(permission => permission.PluginNamespace.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> RequirementWarnings(
        CarbonConfig config,
        IEnumerable<string> permissionIds) =>
        RequirementWarnings(All, config, permissionIds);

    public static IReadOnlyList<string> RequirementWarnings(
        IReadOnlyList<CapabilityPermissionDefinition> catalog,
        CarbonConfig config,
        IEnumerable<string> permissionIds)
    {
        var warnings = new List<string>();
        foreach (var permissionId in permissionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permission = Resolve(catalog, permissionId);
            if (permission is null) continue;

            foreach (var requirement in permission.Requirements)
            {
                if (!IsConfigured(config, requirement.Path))
                    warnings.Add($"permission '{permission.Id}' requires {requirement.Path} - {requirement.Hint}.");
            }
        }
        return warnings;
    }

    public static bool IsKnownPermission(string value) => Resolve(value) is not null;

    private static bool IsConfigured(CarbonConfig config, string path)
    {
        switch (path)
        {
            case "bundle.updater.endpoints": return config.Bundle.Updater.Endpoints.Count > 0;
            case "bundle.updater.publicKey": return !string.IsNullOrWhiteSpace(config.Bundle.Updater.PublicKey);
        }

        // Generic "plugins.<ns>.<prop>" — covers first-party (fs.scopes, http.scope, …) and any
        // third-party requirement a manifest declares. Configured when the property is a non-empty
        // array/string or true.
        var parts = path.Split('.');
        if (parts.Length == 3 && parts[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
            return HasPluginValue(config, parts[1], parts[2]);

        return true; // unknown path shape → don't warn
    }

    private static bool HasPluginValue(CarbonConfig config, string plugin, string property)
    {
        if (!TryGetPluginConfig(config, plugin, out var configElement) ||
            !TryGetProperty(configElement, property, out var propertyElement))
            return false;

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Array => propertyElement.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())),
            JsonValueKind.String => !string.IsNullOrWhiteSpace(propertyElement.GetString()),
            JsonValueKind.True => true,
            _ => false,
        };
    }

    private static bool TryGetPluginConfig(CarbonConfig config, string plugin, out JsonElement element)
    {
        foreach (var (key, value) in config.Plugins)
        {
            if (key.Equals(plugin, StringComparison.OrdinalIgnoreCase))
            {
                element = value;
                return true;
            }
        }

        element = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var item in element.EnumerateObject())
        {
            if (item.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
