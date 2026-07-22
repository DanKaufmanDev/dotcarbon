using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Battery;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Battery.Native;

/// <summary>
/// Registers the platform-native <see cref="IBatteryProvider"/> so <c>BatteryPlugin</c> reads the real
/// device battery on Android/iOS. Call it before <c>Start()</c>, alongside
/// <c>app.UsePlugin&lt;BatteryPlugin&gt;()</c>:
/// <code>app.UseBattery().UsePlugin&lt;BatteryPlugin&gt;();</code>
/// Without this the plugin falls back to the desktop reader.
/// </summary>
public static class BatteryNativeExtensions
{
    public static CarbonApp UseBattery(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<IBatteryProvider>(sp =>
                new NativeBatteryProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
