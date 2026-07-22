#if ANDROID
using Android.Content;
using Android.Locations;
using Android.OS;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Geolocation;

namespace DotCarbon.Plugins.Geolocation.Native;

/// <summary>
/// Android position via <see cref="LocationManager"/>. A cached fix is used when one is available
/// (instant and cheap); otherwise it listens for a single update until the timeout.
/// </summary>
internal sealed class NativeGeolocationProvider : IGeolocationProvider
{
    private readonly AppHandle _app;

    public NativeGeolocationProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? global::Android.App.Application.Context;

    public async Task<GeolocationPosition?> GetCurrentAsync(int timeoutMs, int maximumAgeMs)
    {
        var manager = Context.GetSystemService(Context.LocationService) as LocationManager;
        if (manager is null) return null;

        // A cached fix is only acceptable if the caller allows one that old. The default (0) means the
        // caller wants a genuinely current position, so we always wait for a fresh update.
        if (maximumAgeMs > 0)
        {
            var cached = BestLastKnown(manager);
            if (cached is not null && Java.Lang.JavaSystem.CurrentTimeMillis() - cached.Time <= maximumAgeMs)
                return ToPosition(cached);
        }

        var provider = PreferredProvider(manager);
        if (provider is null) return null;

        var fresh = await RequestSingleAsync(manager, provider, timeoutMs);
        // Fall back to whatever is cached if no fresh fix arrived before the timeout.
        return ToPosition(fresh ?? BestLastKnown(manager));
    }

    private static Location? BestLastKnown(LocationManager manager)
    {
        Location? best = null;
        foreach (var provider in manager.GetProviders(enabledOnly: true) ?? [])
        {
            Location? candidate;
            try { candidate = manager.GetLastKnownLocation(provider); }
            catch (Java.Lang.SecurityException) { return null; } // permission not granted
            if (candidate is null) continue;
            if (best is null || candidate.Time > best.Time) best = candidate;
        }
        return best;
    }

    private static string? PreferredProvider(LocationManager manager)
    {
        var providers = manager.GetProviders(enabledOnly: true) ?? [];
        // GPS is most accurate; network is the usual fallback indoors/on emulators.
        return providers.Contains(LocationManager.GpsProvider) ? LocationManager.GpsProvider
            : providers.Contains(LocationManager.NetworkProvider) ? LocationManager.NetworkProvider
            : providers.FirstOrDefault();
    }

    private static Task<Location?> RequestSingleAsync(LocationManager manager, string provider, int timeoutMs)
    {
        var completion = new TaskCompletionSource<Location?>(TaskCreationOptions.RunContinuationsAsynchronously);
        SingleUpdateListener? listener = null;
        listener = new SingleUpdateListener(location =>
        {
            if (listener is not null) manager.RemoveUpdates(listener);
            completion.TrySetResult(location);
        });

        try
        {
            // Location callbacks need a looper; the main one is always available.
            manager.RequestLocationUpdates(provider, 0L, 0f, listener, Looper.MainLooper);
        }
        catch (Java.Lang.SecurityException)
        {
            return Task.FromResult<Location?>(null); // permission not granted
        }

        _ = Task.Delay(timeoutMs).ContinueWith(_ =>
        {
            manager.RemoveUpdates(listener);
            completion.TrySetResult(null);
        });

        return completion.Task;
    }

    private static GeolocationPosition? ToPosition(Location? location) =>
        location is null
            ? null
            : new GeolocationPosition(
                location.Latitude,
                location.Longitude,
                location.HasAccuracy ? location.Accuracy : null,
                location.HasAltitude ? location.Altitude : null,
                location.HasSpeed ? location.Speed : null,
                location.Time);

    private sealed class SingleUpdateListener : Java.Lang.Object, ILocationListener
    {
        private readonly Action<Location?> _onLocation;

        public SingleUpdateListener(Action<Location?> onLocation) => _onLocation = onLocation;

        public void OnLocationChanged(Location location) => _onLocation(location);
        public void OnProviderDisabled(string provider) { }
        public void OnProviderEnabled(string provider) { }
        public void OnStatusChanged(string? provider, [global::Android.Runtime.GeneratedEnum] Availability status, Bundle? extras) { }
    }
}
#endif
