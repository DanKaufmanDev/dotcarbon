namespace DotCarbon.Plugins.Geolocation;

/// <summary>
/// A position fix. <see cref="Accuracy"/> is the horizontal radius in metres and
/// <see cref="Timestamp"/> is Unix milliseconds. Fields the platform did not report are null.
/// </summary>
public record GeolocationPosition(
    double Latitude,
    double Longitude,
    double? Accuracy,
    double? Altitude,
    double? Speed,
    long Timestamp);

/// <summary>
/// How long to wait for a fix, and how stale a cached fix may be to still count. Mirrors the web
/// Geolocation API's <c>maximumAge</c>: the default 0 means "do not accept a cached fix", which is what
/// a caller asking for the *current* position almost always wants.
/// </summary>
public record CurrentPositionArgs(int TimeoutMs = 10_000, int MaximumAgeMs = 0);

/// <summary>
/// Reads the device position. Mobile-only: a mobile app registers the native provider (Android
/// <c>LocationManager</c> / iOS <c>CLLocationManager</c>) via <c>app.UseGeolocation()</c> from
/// <c>DotCarbon.Plugins.Geolocation.Native</c>. The caller must already hold the "location"
/// permission — request it with the permissions plugin first.
/// </summary>
public interface IGeolocationProvider
{
    /// <summary>
    /// The current position, or null when no fix arrives within the timeout. A cached fix may be
    /// returned only when it is no older than <paramref name="maximumAgeMs"/>.
    /// </summary>
    Task<GeolocationPosition?> GetCurrentAsync(int timeoutMs, int maximumAgeMs);
}
