using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DotCarbon.Core.Config;

namespace DotCarbon.Core.Host;

internal static partial class EmbeddedAssetStore
{
    private const string AssetPrefix = "DotCarbon.Assets/";
    private const string ConfigResource = "DotCarbon.Config/carbon.json";
    private static SecurityConfig _security = new();
    private static string? _localAssetRoot;

    // Mobile apps may not have a managed entry assembly, so locate the assembly that owns the
    // embedded frontend instead of assuming it is the process entry point.
    private static readonly Assembly? AssetAssembly = ResolveAssetAssembly();

    private static readonly IReadOnlyDictionary<string, string> Assets =
        AssetAssembly is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : AssetAssembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(AssetPrefix, StringComparison.Ordinal))
                .ToDictionary(
                    name => name[AssetPrefix.Length..].Replace('\\', '/'),
                    name => name,
                    StringComparer.Ordinal);

    public static bool HasAssets => Assets.Count > 0;

    public static void Configure(SecurityConfig security) => _security = security;

    public static void ConfigureLocalAssets(string? root) =>
        _localAssetRoot = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);

    public static Stream? OpenConfig() => AssetAssembly?.GetManifestResourceStream(ConfigResource);

    private static Assembly? ResolveAssetAssembly()
    {
        static bool HasCarbonResources(Assembly assembly)
        {
            try
            {
                foreach (var name in assembly.GetManifestResourceNames())
                    if (name.StartsWith(AssetPrefix, StringComparison.Ordinal) ||
                        name.Equals(ConfigResource, StringComparison.Ordinal))
                        return true;
            }
            catch
            {
                // A few runtime-provided assemblies do not expose manifest resources.
            }
            return false;
        }

        var entry = Assembly.GetEntryAssembly();
        if (entry is not null && HasCarbonResources(entry)) return entry;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && HasCarbonResources(assembly))
                return assembly;

        // Development hosts can run without embedded assets.
        return entry;
    }

    public static Stream Open(string url, out string contentType)
    {
        var path = GetPath(url);
        if (!_security.AllowSourceMaps &&
            path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            path = "__invalid__";

        if (!TryOpen(path, out var stream) && ShouldUseSpaFallback(path))
        {
            path = "index.html";
            TryOpen(path, out stream);
        }

        contentType = GetContentType(stream is null ? ".txt" : Path.GetExtension(path));
        if (stream is not null &&
            Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase))
            return InjectCsp(stream);

        return stream ?? new MemoryStream("Not found"u8.ToArray(), writable: false);
    }

    private static bool TryOpen(string path, out Stream? stream)
    {
        stream = null;
        if (Assets.TryGetValue(path, out var resource))
            stream = AssetAssembly?.GetManifestResourceStream(resource);
        else if (_localAssetRoot is not null)
            stream = TryOpenLocal(path);

        return stream is not null;
    }

    private static Stream? TryOpenLocal(string path)
    {
        var root = _localAssetRoot;
        if (root is null) return null;

        var target = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, target);
        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            !File.Exists(target))
            return null;

        return File.OpenRead(target);
    }

    /// <summary>Parse a <c>carbon://</c> URL to its cleaned path; false if the URL is invalid.</summary>
    public static bool TryGetPath(string url, out string path)
    {
        path = GetPath(url);
        return path != "__invalid__";
    }

    private static string GetPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "__invalid__";

        if (uri.Scheme != "carbon" ||
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return "__invalid__";

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('\\', '/');
        if (string.IsNullOrEmpty(path)) return "index.html";
        if (path.Length > 512) return "__invalid__";

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part is "." or "..") ? "__invalid__" : string.Join('/', parts);
    }

    private static bool ShouldUseSpaFallback(string path) =>
        path != "__invalid__" && string.IsNullOrEmpty(Path.GetExtension(path));

    internal static string ContentTypeFor(string extension) => GetContentType(extension);

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" or ".map" => "application/json; charset=utf-8",
        ".wasm" => "application/wasm",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".xml" => "application/xml",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };

    private static Stream InjectCsp(Stream stream)
    {
        if (string.IsNullOrWhiteSpace(_security.ContentSecurityPolicy))
            return stream;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var html = ApplyContentSecurityPolicy(reader.ReadToEnd(), _security.ContentSecurityPolicy);
        return new MemoryStream(Encoding.UTF8.GetBytes(html), writable: false);
    }

    /// <summary>
    /// Injects the configured CSP as a <c>&lt;meta&gt;</c> tag, hashing the page's inline scripts/styles
    /// so a strict directive (one without <c>'unsafe-inline'</c>) still allows exactly that bundled
    /// content. A directive that already has <c>'unsafe-inline'</c>, or an author-provided CSP meta, is
    /// left as-is.
    /// </summary>
    internal static string ApplyContentSecurityPolicy(string html, string csp)
    {
        if (html.Contains("http-equiv=\"Content-Security-Policy\"", StringComparison.OrdinalIgnoreCase))
            return html;

        var effective = AugmentDirective(csp, "script-src", InlineHashes(html, InlineScriptRegex()));
        effective = AugmentDirective(effective, "style-src", InlineHashes(html, InlineStyleRegex()));

        var escaped = effective
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        var meta = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{escaped}\">";
        var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        return head >= 0 ? html.Insert(head + "<head>".Length, meta) : meta + html;
    }

    /// <summary>The <c>'sha256-…'</c> source for each inline block the regex matches (deduplicated).</summary>
    private static IReadOnlyList<string> InlineHashes(string html, Regex regex)
    {
        var sources = new List<string>();
        foreach (Match match in regex.Matches(html))
        {
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups[1].Value)));
            var source = $"'sha256-{hash}'";
            if (!sources.Contains(source)) sources.Add(source);
        }
        return sources;
    }

    /// <summary>
    /// Appends the given sources to an existing directive, unless it already allows all inline content
    /// (<c>'unsafe-inline'</c>) — in which case adding a hash would paradoxically disable it. A missing
    /// directive is left untouched.
    /// </summary>
    private static string AugmentDirective(string csp, string directive, IReadOnlyList<string> sources)
    {
        if (sources.Count == 0) return csp;

        var parts = csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var tokens = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || !tokens[0].Equals(directive, StringComparison.OrdinalIgnoreCase))
                continue;

            if (tokens.Any(token => token.Equals("'unsafe-inline'", StringComparison.OrdinalIgnoreCase)))
                return csp;

            var existing = new HashSet<string>(tokens, StringComparer.Ordinal);
            var additions = sources.Where(source => existing.Add(source)).ToList();
            if (additions.Count == 0) return csp;

            parts[i] = parts[i] + " " + string.Join(' ', additions);
            return string.Join("; ", parts);
        }

        return csp;
    }

    [GeneratedRegex(@"<script\b(?![^>]*\ssrc\s*=)[^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InlineScriptRegex();

    [GeneratedRegex(@"<style\b[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InlineStyleRegex();
}
