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
    public static CarbonAssetResponse Serve(string url)
    {
        // Task 4.2: binary command results are served from the store rather than the embedded assets.
        if (EmbeddedAssetStore.TryGetPath(url, out var path) && CarbonBinaryStore.IsBinaryPath(path))
            return CarbonBinaryStore.Serve(path);

        var content = EmbeddedAssetStore.Open(url, out var contentType);
        return new CarbonAssetResponse(content, contentType);
    }
}
