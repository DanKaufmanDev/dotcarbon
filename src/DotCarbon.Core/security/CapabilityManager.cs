using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Security;

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
        _permissionCommands = plugins
            .SelectMany(plugin => plugin.Permissions)
            .GroupBy(permission => permission.Identifier, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.SelectMany(permission => permission.Commands).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
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

    private bool IsCommandAllowed(CarbonWindow window, string command) =>
        GetCapabilityNames(window)
            .Select(name => _config.Security.Capabilities.GetValueOrDefault(name))
            .Where(capability => capability is not null)
            .Cast<CapabilityConfig>()
            .Any(capability => CapabilityAllows(capability, command));

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
        capability.Windows.Any(pattern => WindowLabelMatches(pattern, label));

    /// <summary>
    /// Matches a window label against a capability's <c>windows</c> entry. Supports glob wildcards like
    /// Tauri — <c>*</c> (any run) and <c>?</c> (one character) — so <c>editor-*</c> covers every editor
    /// window. Labels are single tokens, so there are no path-separator semantics.
    /// </summary>
    private static bool WindowLabelMatches(string pattern, string label)
    {
        int p = 0, s = 0, star = -1, mark = 0;
        while (s < label.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == label[s]))
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
        capability.Permissions.Any(permission => PermissionAllows(permission, command));

    private bool PermissionAllows(string permission, string command)
    {
        if (_permissionCommands.TryGetValue(permission, out var patterns))
            return patterns.Any(pattern => CommandPatternMatches(pattern, command));

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
