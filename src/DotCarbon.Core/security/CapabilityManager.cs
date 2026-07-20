using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Security;

/// <summary>
/// The merged <c>allow</c>/<c>deny</c> scope entries a window carries for one permission namespace.
/// The strings are plugin-specific (paths for fs, URLs for http, …); the plugin interprets and enforces.
/// </summary>
public sealed record CarbonPermissionScope(IReadOnlyList<string> Allow, IReadOnlyList<string> Deny)
{
    public static CarbonPermissionScope Empty { get; } = new([], []);
}

internal sealed class CapabilityManager
{
    private readonly CarbonConfig _config;
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _permissionCommands =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    private bool _allowAll;

    public CapabilityManager(CarbonConfig config)
    {
        _config = config;
    }

    public bool IsEnforced { get; private set; }

    public void Configure(bool isDevServer)
    {
        IsEnforced = _config.Security.Enabled;
        _allowAll = isDevServer && _config.Security.DevAllowAll;
    }

    public void SetPluginMetadata(IEnumerable<PluginMetadata> plugins)
    {
        var pluginList = plugins.ToList();
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        // Author-declared permissions (a plugin may split one identifier across several attributes).
        foreach (var group in pluginList
            .SelectMany(plugin => plugin.Permissions)
            .GroupBy(permission => permission.Identifier, StringComparer.Ordinal))
        {
            map[group.Key] = group
                .SelectMany(permission => permission.Commands)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        // Auto-generate a fine-grained permission per command: "<ns>:allow-<command>" grants exactly
        // that command, so a capability can allow a single command without the coarse "<ns>:default".
        // An explicit declaration of the same identifier wins.
        foreach (var plugin in pluginList)
        {
            foreach (var command in plugin.Commands)
            {
                var identifier = $"{plugin.Namespace}:allow-{command.Name.Replace('_', '-')}";
                if (!map.ContainsKey(identifier))
                    map[identifier] = [command.FullName];
            }
        }

        _permissionCommands = map;
    }

    public void EnsureCommandAllowed(CarbonWindow window, string command)
    {
        if (!IsEnforced || _allowAll) return;
        if (IsCommandAllowed(window, command)) return;

        var message =
            $"Permission denied: window '{window.Label}' cannot invoke command '{command}'. " +
            "Add the command to an allowed capability for this window.";
        Console.Error.WriteLine($"[Carbon] {message}");
        throw new UnauthorizedAccessException(message);
    }

    private bool IsCommandAllowed(CarbonWindow window, string command)
    {
        // Local content (carbon://localhost, your dev server) may use any capability that targets the
        // window. Remote content is default-denied — only capabilities that name its URL in `remote`
        // apply. This keeps a page loaded from the web from silently reaching the bridge.
        var localContent = IsLocalContent(window.CurrentUri);
        return ApplicableCapabilities(window)
            .Where(capability => localContent || CapabilityAllowsRemote(capability, window.CurrentUri))
            .Any(capability => CapabilityAllows(capability, command));
    }

    private bool IsLocalContent(Uri? uri)
    {
        // Null means no page has committed yet; the bridge policy already blocks calls in that state,
        // so treat it as local here rather than double-denying.
        if (uri is null) return true;
        if (IsRuntimeOrigin(uri)) return true;
        return !string.IsNullOrWhiteSpace(_config.Build.DevUrl)
            && Uri.TryCreate(_config.Build.DevUrl, UriKind.Absolute, out var dev)
            && OriginEquals(uri, dev);
    }

    private static bool CapabilityAllowsRemote(CapabilityConfig capability, Uri? uri)
    {
        if (uri is null || capability.Remote is null || capability.Remote.Urls.Count == 0)
            return false;

        var origin = Origin(uri);
        var full = uri.ToString();
        return capability.Remote.Urls.Any(pattern => GlobMatch(pattern, origin) || GlobMatch(pattern, full));
    }

    private static bool IsRuntimeOrigin(Uri uri) =>
        uri.Scheme == "carbon" && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

    private static bool OriginEquals(Uri a, Uri b) =>
        string.Equals(Origin(a), Origin(b), StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> GetCapabilityNames(CarbonWindow window)
    {
        foreach (var capability in _config.Security.DefaultCapabilities)
            yield return capability;

        foreach (var capability in window.Options.Capabilities)
            yield return capability;

        foreach (var (name, capability) in _config.Security.Capabilities)
        {
            if (CapabilityTargetsWindow(capability, window.Label))
                yield return name;
        }
    }

    private static bool CapabilityTargetsWindow(CapabilityConfig capability, string label) =>
        capability.Windows.Any(pattern => GlobMatch(pattern, label));

    /// <summary>
    /// Glob-matches a value against a pattern with Tauri-style wildcards — <c>*</c> (any run) and
    /// <c>?</c> (one character). Used for both window labels (<c>editor-*</c>) and remote URLs
    /// (<c>https://*.example.com</c>); the value is treated as one flat string, no path semantics.
    /// </summary>
    private static bool GlobMatch(string pattern, string value)
    {
        int p = 0, s = 0, star = -1, mark = 0;
        while (s < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[s]))
            {
                p++;
                s++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = s;
            }
            else if (star != -1)
            {
                p = star + 1;
                s = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private bool CapabilityAllows(CapabilityConfig capability, string command) =>
        capability.Commands.Any(pattern => CommandPatternMatches(pattern, command)) ||
        capability.Permissions.Any(entry =>
            entry.Identifier is not null && PermissionAllows(entry.Identifier, command));

    /// <summary>
    /// Merges the <c>allow</c>/<c>deny</c> scopes that every capability applicable to <paramref name="window"/>
    /// grants for permissions in <paramref name="permissionNamespace"/> (e.g. "fs"). The scope strings are
    /// opaque here — the owning plugin interprets them. Follows the same local/remote rules as command
    /// checks, so remote content only carries the scopes of capabilities that opted it in.
    /// </summary>
    public CarbonPermissionScope ResolveScope(CarbonWindow window, string permissionNamespace)
    {
        var allow = new List<string>();
        var deny = new List<string>();
        var localContent = IsLocalContent(window.CurrentUri);

        foreach (var capability in ApplicableCapabilities(window))
        {
            if (!localContent && !CapabilityAllowsRemote(capability, window.CurrentUri))
                continue;

            foreach (var entry in capability.Permissions)
            {
                if (entry.Identifier is null || !PermissionInNamespace(entry.Identifier, permissionNamespace))
                    continue;
                allow.AddRange(entry.Allow);
                deny.AddRange(entry.Deny);
            }
        }

        return new CarbonPermissionScope(allow, deny);
    }

    private static bool PermissionInNamespace(string identifier, string permissionNamespace) =>
        string.Equals(identifier, permissionNamespace, StringComparison.Ordinal) ||
        identifier.StartsWith(permissionNamespace + ":", StringComparison.Ordinal);

    private IEnumerable<CapabilityConfig> ApplicableCapabilities(CarbonWindow window) =>
        GetCapabilityNames(window)
            .Select(name => _config.Security.Capabilities.GetValueOrDefault(name))
            .Where(capability => capability is not null)
            .Cast<CapabilityConfig>();

    private bool PermissionAllows(string permission, string command) =>
        PermissionAllows(permission, command, new HashSet<string>(StringComparer.Ordinal));

    private bool PermissionAllows(string permission, string command, HashSet<string> visited)
    {
        if (!visited.Add(permission)) return false; // guard against permission-set cycles

        if (_permissionCommands.TryGetValue(permission, out var patterns))
        {
            foreach (var pattern in patterns)
            {
                if (CommandPatternMatches(pattern, command)) return true;
                // A pattern that names another permission composes it — permission-set composition,
                // so "<ns>:default" can be defined as a list of finer permissions.
                if (_permissionCommands.ContainsKey(pattern) &&
                    PermissionAllows(pattern, command, visited))
                    return true;
            }
            return false;
        }

        // Back-compat: older capability files sometimes used permissions as command patterns.
        return CommandPatternMatches(permission, command);
    }

    private static bool CommandPatternMatches(string pattern, string command)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        if (string.Equals(pattern, command, StringComparison.Ordinal) ||
            pattern == "*" ||
            string.Equals(NormalizeCoreCommand(pattern), command, StringComparison.Ordinal))
            return true;

        if (!pattern.EndsWith(":*", StringComparison.Ordinal)) return false;

        var prefix = pattern[..^1];
        return command.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string NormalizeCoreCommand(string pattern) => pattern switch
    {
        "core:event_emit" => "__carbon:event_emit",
        _ => pattern,
    };
}
