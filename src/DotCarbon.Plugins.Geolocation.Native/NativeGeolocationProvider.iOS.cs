#if IOS
using CoreFoundation;
using CoreLocation;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Geolocation;

namespace DotCarbon.Plugins.Geolocation.Native;

/// <summary>
/// iOS position via <see cref="CLLocationManager"/>. The manager is UIKit-adjacent and delivers through
/// a delegate, so it is created and started on the main queue and the delegate completes the task.
/// </summary>
internal sealed class NativeGeolocationProvider : IGeolocationProvider
{
    public NativeGeolocationProvider(AppHandle app) { }

    public async Task<GeolocationPosition?> GetCurrentAsync(int timeoutMs, int maximumAgeMs)
    {
        // Honour a cached fix only when the caller allows one that old (default 0 = must be fresh).
        if (maximumAgeMs > 0)
        {
            var cached = CLLocationManager.Status == CLAuthorizationStatus.NotDetermined ? null : new CLLocationManager().Location;
            if (cached is not null &&
                DateTime.UtcNow - (DateTime)cached.Timestamp <= TimeSpan.FromMilliseconds(maximumAgeMs))
                return ToPosition(cached);
        }

        var completion = new TaskCompletionSource<CLLocation?>(TaskCreationOptions.RunContinuationsAsynchronously);

        CLLocationManager? manager = null;
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            manager = new CLLocationManager
            {
                DesiredAccuracy = CLLocation.AccuracyBest,
                Delegate = new SingleFixDelegate(location => completion.TrySetResult(location)),
            };
            manager.RequestLocation();
        });

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeoutMs));
        // Stop the manager regardless of whether a fix arrived.
        DispatchQueue.MainQueue.DispatchAsync(() => manager?.StopUpdatingLocation());

        if (finished != completion.Task) return null;
        return ToPosition(await completion.Task);
    }

    private static GeolocationPosition? ToPosition(CLLocation? location)
    {
        if (location is null) return null;
        return new GeolocationPosition(
            location.Coordinate.Latitude,
            location.Coordinate.Longitude,
            location.HorizontalAccuracy >= 0 ? location.HorizontalAccuracy : null,
            location.VerticalAccuracy >= 0 ? location.Altitude : null,
            location.Speed >= 0 ? location.Speed : null,
            (long)(location.Timestamp.SecondsSinceReferenceDate * 1000) + 978307200000L);
    }

    private sealed class SingleFixDelegate : CLLocationManagerDelegate
    {
        private readonly Action<CLLocation?> _onFix;

        public SingleFixDelegate(Action<CLLocation?> onFix) => _onFix = onFix;

        public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations) =>
            _onFix(locations.LastOrDefault());

        public override void Failed(CLLocationManager manager, Foundation.NSError error) => _onFix(null);
    }
}
#endif
