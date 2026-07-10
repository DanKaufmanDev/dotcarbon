using System.Reflection;

namespace DotCarbon.Core.Host;

internal static class EmbeddedAssetStore
{
    private const string AssetPrefix = "DotCarbon.Assets/";
    private const string ConfigResource = "DotCarbon.Config/carbon.json";

    private static readonly Assembly EntryAssembly = Assembly.GetEntryAssembly()
        ?? throw new InvalidOperationException("DotCarbon could not locate the application assembly.");

    private static readonly IReadOnlyDictionary<string, string> Assets = EntryAssembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(AssetPrefix, StringComparison.Ordinal))
        .ToDictionary(
            name => name[AssetPrefix.Length..].Replace('\\', '/'),
            name => name,
            StringComparer.Ordinal);

    public static bool HasAssets => Assets.Count > 0;

    public static Stream? OpenConfig() => EntryAssembly.GetManifestResourceStream(ConfigResource);

    public static Stream Open(object sender, string scheme, string url, out string contentType)
    {
        var path = GetPath(url);
        if (!TryOpen(path, out var stream) && ShouldUseSpaFallback(path))
        {
            path = "index.html";
            TryOpen(path, out stream);
        }

        contentType = GetContentType(stream is null ? ".txt" : Path.GetExtension(path));
        return stream ?? new MemoryStream("Not found"u8.ToArray(), writable: false);
    }

    private static bool TryOpen(string path, out Stream? stream)
    {
        stream = Assets.TryGetValue(path, out var resource)
            ? EntryAssembly.GetManifestResourceStream(resource)
            : null;
        return stream is not null;
    }

    private static string GetPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "index.html";

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('\\', '/');
        if (string.IsNullOrEmpty(path)) return "index.html";

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part is "." or "..") ? "__invalid__" : string.Join('/', parts);
    }

    private static bool ShouldUseSpaFallback(string path) =>
        path != "__invalid__" && string.IsNullOrEmpty(Path.GetExtension(path));

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
}
