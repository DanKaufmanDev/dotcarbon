namespace DotCarbon.Core.Host;

/// <summary>
/// The allow-list for serving local files over <c>carbon://</c> via <c>convertFileSrc</c> (Task 4.6).
/// A file is served only if it resolves inside one of the configured roots, so showing a user's image
/// doesn't hand the webview the whole filesystem. Roots accept the same shortcuts as the fs plugin's
/// scopes (<c>$appdata</c>, <c>$documents</c>, <c>~</c>, absolute paths); empty denies everything.
/// </summary>
public static class CarbonAssetScope
{
    private static string[] _roots = [];

    public static void Configure(string[]? roots) => _roots = roots ?? [];

    /// <summary>Whether <paramref name="fullPath"/> (already absolute) sits inside an allowed root.</summary>
    public static bool IsAllowed(string fullPath)
    {
        if (_roots
            .Select(ResolveRoot)
            .Where(root => root.Length > 0)
            .Any(root => IsWithin(fullPath, root)))
            return true;

        // Also honor roots granted at runtime and restored by the persisted-scope plugin (Task 6.9).
        return CarbonRuntimeScope.IsAllowed(CarbonRuntimeScope.Asset, fullPath);
    }

    private static string ResolveRoot(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return string.Empty;

        var value = scope.Trim();
        var root = value.TrimStart('$').ToLowerInvariant() switch
        {
            "appdata" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "temp" or "tmp" => Path.GetTempPath(),
            _ => ExpandHome(value),
        };
        return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.GetFullPath(root);
    }

    private static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            return Path.Combine(home, path[2..]);
        return path;
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(normalizedRoot, PathComparison) ||
            path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
