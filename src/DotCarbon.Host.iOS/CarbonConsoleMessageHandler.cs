using Foundation;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>Receives forwarded WebView console messages and writes them to the app process output.</summary>
public sealed class CarbonConsoleMessageHandler : NSObject, IWKScriptMessageHandler
{
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        var text = message.Body?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        Console.WriteLine(text);
        Console.Out.Flush();
    }
}
