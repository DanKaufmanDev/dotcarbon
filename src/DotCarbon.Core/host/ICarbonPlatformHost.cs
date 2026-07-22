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

    /// <summary>
    /// The host's native handle, for mobile plugin bindings that call platform APIs — the Android
    /// <c>Context</c> or the iOS <c>WKWebView</c>. It is typed as <see cref="object"/> so the
    /// platform-neutral core carries it without referencing Android/iOS types; a native binding casts
    /// it back (e.g. <c>app.PlatformNativeHandle as Android.Content.Context</c>). Null on desktop.
    /// </summary>
    object? NativeHandle => null;

    /// <summary>
    /// The host's native dialogs, or null when it provides none (the Dialog plugin then reports that
    /// dialogs are unavailable rather than failing obscurely).
    /// </summary>
    ICarbonDialogs? Dialogs => null;

    /// <summary>
    /// The host's runtime permission prompts, or null when the platform does not gate capabilities
    /// (desktop), in which case callers treat permissions as already granted.
    /// </summary>
    ICarbonPermissions? Permissions => null;
}
