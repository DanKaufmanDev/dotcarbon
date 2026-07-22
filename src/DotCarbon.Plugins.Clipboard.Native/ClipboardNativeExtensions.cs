using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Clipboard;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Clipboard.Native;

/// <summary>
/// Registers the platform-native <see cref="IClipboardProvider"/> so <c>ClipboardPlugin</c> uses the
/// device clipboard on Android/iOS. Call before <c>Start()</c>:
/// <code>app.UseClipboard().UsePlugin&lt;ClipboardPlugin&gt;();</code>
/// </summary>
public static class ClipboardNativeExtensions
{
    public static CarbonApp UseClipboard(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<IClipboardProvider>(sp =>
                new NativeClipboardProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
