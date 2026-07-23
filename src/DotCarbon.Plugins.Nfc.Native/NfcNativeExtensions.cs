using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Nfc;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Nfc.Native;

/// <summary>
/// Registers the platform-native <see cref="INfcProvider"/> so <c>NfcPlugin</c> reads real tags:
/// <code>app.UseNfc().UsePlugin&lt;NfcPlugin&gt;();</code>
/// Android needs the NFC manifest permission; iOS needs the
/// <c>com.apple.developer.nfc.readersession.formats</c> entitlement and an
/// <c>NFCReaderUsageDescription</c>, and only works on a physical device.
/// </summary>
public static class NfcNativeExtensions
{
    public static CarbonApp UseNfc(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<INfcProvider>(sp =>
                new NativeNfcProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
