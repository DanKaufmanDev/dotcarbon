namespace DotCarbon.Core.Host;

/// <summary>
/// A platform's window/webview provider and run loop. The desktop package ships a
/// Photino implementation; android/ios ship their own. <see cref="CarbonApp"/> is given
/// one via <c>UsePlatform</c> (e.g. the desktop package's <c>UseDesktop()</c>).
/// </summary>
public interface ICarbonPlatformHost
{
    /// <summary>Create a native webview for a window, wiring the runtime callbacks.</summary>
    ICarbonWebView CreateWebView(CarbonWebViewContext context);

    /// <summary>Run the platform message loop until the main webview closes (blocking).</summary>
    void Run(ICarbonWebView mainWebView);
}
