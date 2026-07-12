namespace DotCarbon.Plugins.Http;

/// <summary>
/// Allowlist matching for the http plugin. An empty scope allows everything (convenient for dev);
/// configure <c>plugins.http.scope</c> in production. A trailing <c>*</c> is a path prefix wildcard.
/// </summary>
public static class HttpScope
{
    public static bool IsAllowed(string url, IReadOnlyList<string> scope)
    {
        if (scope.Count == 0) return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttp(uri))
            return false;

        return scope.Any(pattern => Matches(uri, pattern));
    }

    private static bool Matches(Uri uri, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var wildcard = pattern.EndsWith('*');
        var candidate = wildcard ? pattern[..^1] : pattern;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var allowed) || !IsHttp(allowed))
            return false;

        if (!SameOrigin(uri, allowed))
            return false;

        if (!wildcard)
            return SamePathAndQuery(uri, allowed);

        var prefix = allowed.PathAndQuery;
        if (prefix.EndsWith('/'))
            return uri.PathAndQuery.StartsWith(prefix, StringComparison.Ordinal);

        return uri.PathAndQuery.Equals(prefix, StringComparison.Ordinal) ||
               uri.PathAndQuery.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool SameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static bool SamePathAndQuery(Uri left, Uri right) =>
        left.AbsolutePath.TrimEnd('/').Equals(right.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal) &&
        left.Query.Equals(right.Query, StringComparison.Ordinal);
}
