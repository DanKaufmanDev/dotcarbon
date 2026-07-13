using DotCarbon.Core.Host;
using DotCarbon.Core.Runtime;
using Photino.NET;

namespace DotCarbon.Host.Desktop;

/// <summary>Desktop entry points and native-interop helpers for Carbon apps.</summary>
public static class DesktopExtensions
{
    /// <summary>Run this Carbon app on the desktop (Photino). Call before <c>Run()</c>.</summary>
    public static CarbonApp UseDesktop(this CarbonApp app)
    {
        var configured = app.UsePlatform(new PhotinoPlatformHost());
        if (DesktopSmoke.Enabled)
            configured.Setup(DesktopSmoke.Arm);
        return configured;
    }

    /// <summary>The underlying Photino window for a Carbon window (desktop-only plugins).</summary>
    public static PhotinoWindow Photino(this CarbonWindow window) => window.Native.Photino();

    /// <summary>The underlying Photino window for a Carbon webview (desktop-only plugins).</summary>
    public static PhotinoWindow Photino(this ICarbonWebView view) =>
        view is PhotinoWebView desktop
            ? desktop.Window
            : throw new InvalidOperationException(
                "This webview is not a desktop (Photino) window. Native window access is desktop-only.");
}
