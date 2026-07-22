using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Geolocation;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Geolocation.Native;

/// <summary>
/// Registers the platform-native <see cref="IGeolocationProvider"/> so <c>GeolocationPlugin</c> reads a
/// real position. Call before <c>Start()</c>:
/// <code>app.UseGeolocation().UsePlugin&lt;GeolocationPlugin&gt;();</code>
/// The app also needs the location permission (<c>permissions.location</c> in carbon.json) and should
/// request it at runtime via the permissions plugin.
/// </summary>
public static class GeolocationNativeExtensions
{
    public static CarbonApp UseGeolocation(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<IGeolocationProvider>(sp =>
                new NativeGeolocationProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
