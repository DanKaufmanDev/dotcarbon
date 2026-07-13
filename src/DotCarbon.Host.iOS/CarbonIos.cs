namespace DotCarbon.Host.iOS;

/// <summary>Shared constants for the iOS bridge transport.</summary>
public static class CarbonIos
{
    /// <summary>Name the WKScriptMessageHandler is registered under.</summary>
    public const string MessageHandlerName = "carbonNative";

    /// <summary>Name the WKScriptMessageHandler that receives forwarded WebView console messages.</summary>
    public const string ConsoleHandlerName = "carbonConsole";

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

    /// <summary>Forwards console.log/warn/error to native stdout so simulator smokes can assert JS boot.</summary>
    public const string ConsoleShim =
        "(function () {" +
        "  function stringify(item) {" +
        "    if (typeof item === 'string') return item;" +
        "    try { return JSON.stringify(item); } catch (_) { return String(item); }" +
        "  }" +
        "  ['log','warn','error'].forEach(function (level) {" +
        "    var original = console[level];" +
        "    console[level] = function () {" +
        "      var message = Array.prototype.map.call(arguments, stringify).join(' ');" +
        "      try { window.webkit.messageHandlers." + ConsoleHandlerName + ".postMessage(message); } catch (_) {}" +
        "      if (original) original.apply(console, arguments);" +
        "    };" +
        "  });" +
        "})();";
}
