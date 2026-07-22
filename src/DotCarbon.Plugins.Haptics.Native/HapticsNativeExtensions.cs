using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Haptics;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Haptics.Native;

/// <summary>
/// Registers the platform-native <see cref="IHapticsProvider"/> so <c>HapticsPlugin</c> plays real
/// feedback on Android/iOS. Call before <c>Start()</c>:
/// <code>app.UseHaptics().UsePlugin&lt;HapticsPlugin&gt;();</code>
/// Android also needs the VIBRATE permission (<c>permissions.vibrate</c> in carbon.json).
/// </summary>
public static class HapticsNativeExtensions
{
    public static CarbonApp UseHaptics(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<IHapticsProvider>(sp =>
                new NativeHapticsProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
