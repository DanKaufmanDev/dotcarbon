using System.Collections.Concurrent;

namespace DotCarbon.Core.Host;

/// <summary>
/// Holds binary command results until the frontend fetches them over <c>carbon://</c> (Task 4.2).
///
/// Photino's web-message channel is text-only, so returning bytes through <c>invoke</c> would mean
/// base64 in JSON. Instead a command returns a <see cref="Bridge.CarbonBinary"/>; its bytes are parked
/// here under a token and the command result is a <c>carbon://localhost/__binary__/&lt;token&gt;</c>
/// URL. The frontend fetches that URL and gets the raw bytes with no base64 overhead — the same shape
/// as Tauri's custom-protocol binary responses.
///
/// Entries are one-shot: taken (and evicted) on first fetch, so nothing lingers. A cap bounds the map
/// in case a produced result is never fetched.
/// </summary>
public static class CarbonBinaryStore
{
    private const string RoutePrefix = "__binary__/";
    private const int MaxPending = 256;

    private static readonly ConcurrentDictionary<string, Entry> Pending = new();

    private readonly record struct Entry(byte[] Data, string ContentType);

    /// <summary>Park bytes and return the <c>carbon://</c> URL that will serve them.</summary>
    public static string Register(byte[] data, string contentType)
    {
        // Bound the map: if a lot of results are produced but never fetched, drop the oldest-ish few.
        if (Pending.Count >= MaxPending)
            foreach (var key in Pending.Keys.Take(Pending.Count - MaxPending + 1))
                Pending.TryRemove(key, out _);

        var token = Guid.NewGuid().ToString("N");
        Pending[token] = new Entry(data, contentType);
        return $"carbon://localhost/{RoutePrefix}{token}";
    }

    /// <summary>True if <paramref name="path"/> is a binary route (as returned by <see cref="Serve"/>).</summary>
    public static bool IsBinaryPath(string path) => path.StartsWith(RoutePrefix, StringComparison.Ordinal);

    /// <summary>Serve a parked binary by its route path, evicting it (one-shot).</summary>
    public static CarbonAssetResponse Serve(string path)
    {
        var token = path[RoutePrefix.Length..];
        if (Pending.TryRemove(token, out var entry))
            return new CarbonAssetResponse(new MemoryStream(entry.Data, writable: false), entry.ContentType);
        return new CarbonAssetResponse(new MemoryStream("Not found"u8.ToArray(), writable: false), "text/plain");
    }
}
