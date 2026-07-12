using System.Reflection;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Plugins;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CarbonPluginAttribute(
    string? name = null,
    string? version = null,
    string? description = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Version { get; } = version;
    public string? Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CarbonPermissionAttribute(
    string identifier,
    string? description = null) : Attribute
{
    public string Identifier { get; } = identifier;
    public string? Description { get; } = description;
    public string[] Commands { get; set; } = [];
}

/// <summary>
/// Declares which platforms a plugin supports (<c>desktop</c>, <c>android</c>, <c>ios</c>).
/// Unset means all platforms. Surfaced in plugin metadata and checked by the bundler / <c>carbon doctor</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CarbonPluginPlatformAttribute(params string[] platforms) : Attribute
{
    public string[] Platforms { get; } = platforms;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CarbonEventAttribute(
    string name,
    string? payloadType = null,
    string? description = null) : Attribute
{
    public string Name { get; } = name;
    public string? PayloadType { get; } = payloadType;
    public string? Description { get; } = description;
}

public sealed record PluginContext(AppHandle App, JsonElement? Configuration)
{
    public bool HasConfiguration =>
        Configuration is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

    public TOptions GetConfiguration<TOptions>(JsonTypeInfo<TOptions> typeInfo)
    {
        if (!HasConfiguration)
            throw new InvalidOperationException("No plugin configuration was supplied.");

        return Configuration!.Value.Deserialize(typeInfo)
            ?? throw new InvalidOperationException(
                $"Plugin configuration could not be deserialized as {typeof(TOptions).Name}.");
    }

    [RequiresUnreferencedCode("Use GetConfiguration with JsonTypeInfo for trimmed applications.")]
    [RequiresDynamicCode("Use GetConfiguration with JsonTypeInfo for NativeAOT applications.")]
    public TOptions GetConfiguration<TOptions>()
    {
        if (!HasConfiguration)
            throw new InvalidOperationException("No plugin configuration was supplied.");

        return Configuration!.Value.Deserialize<TOptions>()
            ?? throw new InvalidOperationException(
                $"Plugin configuration could not be deserialized as {typeof(TOptions).Name}.");
    }
}

public sealed record PluginMetadata(
    string Namespace,
    string Name,
    string? Version = null,
    string? Description = null,
    IReadOnlyList<PluginCommandMetadata>? Commands = null,
    IReadOnlyList<PluginPermissionMetadata>? Permissions = null,
    IReadOnlyList<PluginEventMetadata>? Events = null,
    IReadOnlyList<string>? Platforms = null)
{
    public IReadOnlyList<PluginCommandMetadata> Commands { get; init; } = Commands ?? [];
    public IReadOnlyList<PluginPermissionMetadata> Permissions { get; init; } = Permissions ?? [];
    public IReadOnlyList<PluginEventMetadata> Events { get; init; } = Events ?? [];
    public IReadOnlyList<string> Platforms { get; init; } = Platforms ?? ["desktop", "android", "ios"];

    public static PluginMetadata FromPlugin(IPlugin plugin)
    {
        var type = plugin.GetType();
        var pluginAttribute = type.GetCustomAttribute<CarbonPluginAttribute>();
        var platformAttribute = type.GetCustomAttribute<CarbonPluginPlatformAttribute>();
        var assemblyName = type.Assembly.GetName();

        return new PluginMetadata(
            plugin.Namespace,
            pluginAttribute?.Name ?? type.Name,
            pluginAttribute?.Version ?? assemblyName.Version?.ToString(),
            pluginAttribute?.Description,
            [],
            ReadPermissions(type),
            ReadEvents(type),
            platformAttribute?.Platforms);
    }

    private static IReadOnlyList<PluginPermissionMetadata> ReadPermissions(Type type) =>
        type.GetCustomAttributes<CarbonPermissionAttribute>()
            .Select(permission => new PluginPermissionMetadata(
                permission.Identifier,
                permission.Description,
                permission.Commands))
            .ToArray();

    private static IReadOnlyList<PluginEventMetadata> ReadEvents(Type type) =>
        type.GetCustomAttributes<CarbonEventAttribute>()
            .Select(evt => new PluginEventMetadata(
                evt.Name,
                evt.PayloadType,
                evt.Description))
            .ToArray();
}

public sealed record PluginCommandMetadata(
    string Name,
    string FullName,
    string? Arguments,
    string? Result);

public sealed record PluginPermissionMetadata(
    string Identifier,
    string? Description = null,
    IReadOnlyList<string>? Commands = null)
{
    public IReadOnlyList<string> Commands { get; init; } = Commands ?? [];
}

public sealed record PluginEventMetadata(
    string Name,
    string? PayloadType = null,
    string? Description = null);
