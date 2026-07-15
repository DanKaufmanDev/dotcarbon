using Foundation;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Receives frontend bridge messages through WKWebView's <c>carbonNative</c> handler.
/// </summary>
public sealed class CarbonScriptMessageHandler : NSObject, IWKScriptMessageHandler
{
    private readonly Action<string> _onMessage;

    public CarbonScriptMessageHandler(Action<string> onMessage) => _onMessage = onMessage;

    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message) =>
        _onMessage(message.Body?.ToString() ?? string.Empty);
}
