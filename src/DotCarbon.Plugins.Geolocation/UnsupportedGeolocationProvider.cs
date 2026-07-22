namespace DotCarbon.Plugins.Geolocation;

/// <summary>
/// Fallback when no native provider is registered. Unlike haptics — where a silent no-op is the right
/// desktop behaviour — a caller asking "where am I?" must not receive a fake answer, so this reports
/// clearly instead.
/// </summary>
internal sealed class UnsupportedGeolocationProvider : IGeolocationProvider
{
    public Task<GeolocationPosition?> GetCurrentAsync(int timeoutMs, int maximumAgeMs) =>
        throw new NotSupportedException(
            "Geolocation is not available on this platform. On Android/iOS call app.UseGeolocation() " +
            "(DotCarbon.Plugins.Geolocation.Native) to register the native provider.");
}
