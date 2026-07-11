using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// The Android <see cref="ICarbonPlatformHost"/>. Android hosts a single WebView owned by the
/// <see cref="CarbonActivity"/>, so <see cref="CreateWebView"/> returns that one view and binds the
/// runtime callbacks to it. <see cref="Run"/> is a no-op: the Android runtime (the Activity + its
/// message loop) drives the app, so <c>CarbonApp.Start()</c> is used instead of the blocking Run.
/// </summary>
public sealed class AndroidPlatformHost : ICarbonPlatformHost
{
    private readonly AndroidWebView _mainWebView;

    public AndroidPlatformHost(AndroidWebView mainWebView) => _mainWebView = mainWebView;

    public ICarbonWebView CreateWebView(CarbonWebViewContext context)
    {
        if (context.Parent is not null)
            throw new NotSupportedException("Additional windows are not supported on Android.");

        _mainWebView.Attach(context.Callbacks);
        context.Callbacks.Creating?.Invoke();
        return _mainWebView;
    }

    public void Run(ICarbonWebView mainWebView)
    {
        // Android drives its own loop via the Activity; nothing to block on here.
    }
}
