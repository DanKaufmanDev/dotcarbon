using System.Text.Json;
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

    public static CapabilityPermissionDefinition? Resolve(string value)
    {
        foreach (var permission in All)
        {
            if (permission.Id.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                permission.PluginNamespace.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                permission.Id.Equals(value + ":default", StringComparison.OrdinalIgnoreCase))
                return permission;
        }

        return null;
    }

    public static IReadOnlyList<string> RequirementWarnings(
        CarbonConfig config,
        IEnumerable<string> permissionIds)
    {
        var warnings = new List<string>();
        foreach (var permissionId in permissionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permission = Resolve(permissionId);
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

    private static bool IsConfigured(CarbonConfig config, string path) => path switch
    {
        "plugins.fs.scopes" => HasPluginArray(config, "fs", "scopes"),
        "plugins.http.scope" => HasPluginArray(config, "http", "scope"),
        "plugins.shell.allowedPrograms" => HasPluginArray(config, "shell", "allowedPrograms"),
        "bundle.updater.endpoints" => config.Bundle.Updater.Endpoints.Count > 0,
        "bundle.updater.publicKey" => !string.IsNullOrWhiteSpace(config.Bundle.Updater.PublicKey),
        _ => true
    };

    private static bool HasPluginArray(CarbonConfig config, string plugin, string property)
    {
        if (!TryGetPluginConfig(config, plugin, out var configElement) ||
            !TryGetProperty(configElement, property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.Array)
            return false;

        return propertyElement.EnumerateArray()
            .Any(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()));
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
