using DotCarbon.Core.Host;

namespace DotCarbon.Host.Desktop;

/// <summary>The desktop platform host: creates Photino webviews and runs the Photino message loop.</summary>
public sealed class PhotinoPlatformHost : ICarbonPlatformHost
{
    public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new PhotinoWebView(context);

    public void Run(ICarbonWebView mainWebView) => ((PhotinoWebView)mainWebView).Window.WaitForClose();
}
