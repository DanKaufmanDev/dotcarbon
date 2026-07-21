namespace DotCarbon.Core.Host;

/// <summary>
/// Scope roots granted while the app runs (Task 6.9) — e.g. a folder the user just picked in a dialog —
/// keyed by scope ("fs" or "asset"). Unlike the static config scopes, these are added at runtime; the
/// persisted-scope plugin saves and restores them across launches. <see cref="CarbonAssetScope"/> and the
/// fs plugin consult this in addition to their configured scopes.
/// </summary>
public static class CarbonRuntimeScope
{
    public const string FileSystem = "fs";
    public const string Asset = "asset";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<string>> Roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Grant a directory (or file) root for a scope. Paths accept <c>$appdata</c>/<c>~</c> shortcuts.</summary>
    public static void Allow(string scope, string path)
    {
        var root = ResolveRoot(path);
        if (root.Length == 0) return;
        lock (Gate)
        {
            if (!Roots.TryGetValue(scope, out var list)) Roots[scope] = list = [];
            if (!list.Any(existing => existing.Equals(root, PathComparison)))
                list.Add(root);
        }
    }

    /// <summary>The granted roots for a scope.</summary>
    public static IReadOnlyList<string> Entries(string scope)
    {
        lock (Gate) return Roots.TryGetValue(scope, out var list) ? [.. list] : [];
    }

    public static bool HasEntries(string scope)
    {
        lock (Gate) return Roots.TryGetValue(scope, out var list) && list.Count > 0;
    }

    /// <summary>Whether <paramref name="fullPath"/> (already absolute) sits inside a granted root.</summary>
    public static bool IsAllowed(string scope, string fullPath)
    {
        lock (Gate)
        {
            return Roots.TryGetValue(scope, out var list) && list.Any(root => IsWithin(fullPath, root));
        }
    }

    /// <summary>Forget all granted roots (used by tests and the plugin's clear command).</summary>
    public static void Clear()
    {
        lock (Gate) Roots.Clear();
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
