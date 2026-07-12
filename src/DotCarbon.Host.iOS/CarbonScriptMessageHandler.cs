using Foundation;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// The JS→native side of the bridge. Registered on the WKWebView's user content controller as
/// <c>carbonNative</c>; the shim in <see cref="CarbonIos.BridgeShim"/> routes
/// <c>window.external.sendMessage</c> here via <c>window.webkit.messageHandlers</c>.
/// </summary>
public sealed class CarbonScriptMessageHandler : NSObject, IWKScriptMessageHandler
{
    private readonly Action<string> _onMessage;

    public CarbonScriptMessageHandler(Action<string> onMessage) => _onMessage = onMessage;

    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message) =>
        _onMessage(message.Body?.ToString() ?? string.Empty);
}
