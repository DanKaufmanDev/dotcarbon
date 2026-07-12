namespace DotCarbon.Host.iOS;

/// <summary>Shared constants for the iOS bridge transport.</summary>
public static class CarbonIos
{
    /// <summary>Name the WKScriptMessageHandler is registered under.</summary>
    public const string MessageHandlerName = "carbonNative";

    /// <summary>
    /// Injected at document start so <c>@dotcarbon/api</c> works unchanged: it expects Photino's
    /// <c>window.external.sendMessage</c> / <c>receiveMessage</c>. Here those map to the WKWebView
    /// message handler (JS→native) and a receiver the native side calls via evaluateJavaScript.
    /// </summary>
    public const string BridgeShim =
        "(function () {" +
        "  var receiver = null;" +
        "  window.external = {" +
        "    sendMessage: function (message) { window.webkit.messageHandlers." + MessageHandlerName + ".postMessage(message); }," +
        "    receiveMessage: function (callback) { receiver = callback; }" +
        "  };" +
        "  window.__carbonReceive = function (message) { if (receiver) { receiver(message); } };" +
        "})();";
}
