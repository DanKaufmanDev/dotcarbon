using DotCarbon.Core.Host;

namespace DotCarbon.Host.Desktop;

/// <summary>The desktop platform host: creates Photino webviews and runs the Photino message loop.</summary>
public sealed class PhotinoPlatformHost : ICarbonPlatformHost
{
    private PhotinoWebView? _mainWebView;
    private PhotinoDialogs? _dialogs;

    public ICarbonWebView CreateWebView(CarbonWebViewContext context)
    {
        var view = new PhotinoWebView(context);
        // Dialogs are parented to the first (main) window.
        _mainWebView ??= view;
        return view;
    }

    public void Run(ICarbonWebView mainWebView) => ((PhotinoWebView)mainWebView).Window.WaitForClose();

    public ICarbonDialogs? Dialogs =>
        _mainWebView is null ? null : _dialogs ??= new PhotinoDialogs(_mainWebView.Window);
}
