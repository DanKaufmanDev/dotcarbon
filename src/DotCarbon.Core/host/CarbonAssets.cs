namespace DotCarbon.Core.Host;

/// <summary>A resolved <c>carbon://</c> asset: its content stream and MIME type.</summary>
public sealed record CarbonAssetResponse(Stream Content, string ContentType);

/// <summary>
/// Public entry point a platform host uses to serve <c>carbon://localhost/…</c> requests.
/// Resolution, path safety, SPA fallback and CSP injection all live in Core; the platform
/// host only binds this to its native custom-scheme mechanism.
/// </summary>
public static class CarbonAssets
{
    private const string AssetMarker = "carbon://localhost/__asset__/";

    public static CarbonAssetResponse Serve(string url)
    {
        // Task 4.6: convertFileSrc URLs serve a local file, scope-checked, from disk.
        if (TryParseAssetRoute(url, out var filePath))
            return ServeLocalFile(filePath);

        // Task 4.2: binary command results are served from the store rather than the embedded assets.
        if (EmbeddedAssetStore.TryGetPath(url, out var path) && CarbonBinaryStore.IsBinaryPath(path))
            return CarbonBinaryStore.Serve(path);

        var content = EmbeddedAssetStore.Open(url, out var contentType);
        return new CarbonAssetResponse(content, contentType);
    }

    private static bool TryParseAssetRoute(string url, out string filePath)
    {
        filePath = string.Empty;
        if (!url.StartsWith(AssetMarker, StringComparison.OrdinalIgnoreCase)) return false;

        // The path was encodeURIComponent'd into a single opaque segment, so read the raw URL rather
        // than Uri.AbsolutePath (which mangles %2F) and unescape it back to the real path.
        var encoded = url[AssetMarker.Length..];
        var cut = encoded.IndexOfAny(['?', '#']);
        if (cut >= 0) encoded = encoded[..cut];
        filePath = Uri.UnescapeDataString(encoded);
        return filePath.Length > 0;
    }

    private static CarbonAssetResponse ServeLocalFile(string filePath)
    {
        string fullPath;
        try { fullPath = System.IO.Path.GetFullPath(filePath); }
        catch { return Forbidden(); }

        if (!CarbonAssetScope.IsAllowed(fullPath) || !File.Exists(fullPath))
            return Forbidden();

        return new CarbonAssetResponse(
            File.OpenRead(fullPath), EmbeddedAssetStore.ContentTypeFor(System.IO.Path.GetExtension(fullPath)));
    }

    private static CarbonAssetResponse Forbidden() =>
        new(new MemoryStream("Forbidden"u8.ToArray(), writable: false), "text/plain");
}
