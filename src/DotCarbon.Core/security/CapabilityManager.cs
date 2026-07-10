using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Security;

internal sealed class CapabilityManager
{
    private readonly CarbonConfig _config;
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
        capability.Windows.Any(window =>
            window == "*" || string.Equals(window, label, StringComparison.Ordinal));

    private static bool CapabilityAllows(CapabilityConfig capability, string command) =>
        capability.Commands.Concat(capability.Permissions)
            .Any(pattern => CommandPatternMatches(pattern, command));

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
