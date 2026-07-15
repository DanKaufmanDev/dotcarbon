using DotCarbon.Core.Host;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Connects CarbonApp to the WKWebView and lifecycle owned by <see cref="CarbonAppDelegate"/>.
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
        // UIApplication owns the platform message loop.
    }
}
