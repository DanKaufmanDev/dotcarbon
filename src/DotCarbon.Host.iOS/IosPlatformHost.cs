using DotCarbon.Core.Host;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Connects CarbonApp to the WKWebView and lifecycle owned by <see cref="CarbonAppDelegate"/>.
/// </summary>
public sealed class IosPlatformHost : ICarbonPlatformHost
{
    private readonly IosWebView _mainWebView;

    public IosPlatformHost(IosWebView mainWebView) => _mainWebView = mainWebView;

    /// <summary>The native <see cref="global::WebKit.WKWebView"/>, for native plugin bindings.</summary>
    public object? NativeHandle => _mainWebView.Native;

    /// <summary>Native iOS dialogs, presented on the webview's root view controller.</summary>
    public ICarbonDialogs? Dialogs =>
        _dialogs ??= new IosDialogs(() => _mainWebView.Native.Window?.RootViewController);

    private IosDialogs? _dialogs;

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
