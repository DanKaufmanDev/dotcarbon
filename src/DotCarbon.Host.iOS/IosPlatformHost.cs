using DotCarbon.Core.Host;

namespace DotCarbon.Host.iOS;

/// <summary>
/// The iOS <see cref="ICarbonPlatformHost"/>. iOS hosts a single WKWebView owned by the
/// <see cref="CarbonAppDelegate"/>, so <see cref="CreateWebView"/> returns that one view and binds the
/// runtime callbacks to it. <see cref="Run"/> is a no-op: UIApplication drives the loop, so
/// <c>CarbonApp.Start()</c> is used instead of the blocking Run.
/// </summary>
public sealed class IosPlatformHost : ICarbonPlatformHost
{
    private readonly IosWebView _mainWebView;

    public IosPlatformHost(IosWebView mainWebView) => _mainWebView = mainWebView;

    public ICarbonWebView CreateWebView(CarbonWebViewContext context)
    {
        if (context.Parent is not null)
            throw new NotSupportedException("Additional windows are not supported on iOS.");

        _mainWebView.Attach(context.Callbacks);
        context.Callbacks.Creating?.Invoke();
        return _mainWebView;
    }

    public void Run(ICarbonWebView mainWebView)
    {
        // UIApplication drives its own loop; nothing to block on here.
    }
}
