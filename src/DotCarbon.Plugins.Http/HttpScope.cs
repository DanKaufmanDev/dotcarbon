namespace DotCarbon.Plugins.Http;

/// <summary>
/// Allowlist matching for the http plugin. An empty scope allows everything (convenient for dev);
/// configure <c>plugins.http.scope</c> in production. A trailing <c>*</c> is a prefix wildcard.
/// </summary>
public static class HttpScope
{
    public static bool IsAllowed(string url, IReadOnlyList<string> scope)
    {
        if (scope.Count == 0) return true;
        return scope.Any(pattern => Matches(url, pattern));
    }

    private static bool Matches(string url, string pattern)
    {
        var prefix = pattern.EndsWith('*') ? pattern[..^1] : pattern;
        return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
