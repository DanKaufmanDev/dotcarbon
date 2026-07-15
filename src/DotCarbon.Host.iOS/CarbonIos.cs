namespace DotCarbon.Host.iOS;

/// <summary>Shared constants for the iOS bridge transport.</summary>
public static class CarbonIos
{
    /// <summary>Name the WKScriptMessageHandler is registered under.</summary>
    public const string MessageHandlerName = "carbonNative";

    /// <summary>Name the WKScriptMessageHandler that receives forwarded WebView console messages.</summary>
    public const string ConsoleHandlerName = "carbonConsole";

    /// <summary>
    /// Installs the bridge shape expected by <c>@dotcarbon/api</c> over WKWebView message handlers.
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

    /// <summary>Forwards frontend console output to the native process log.</summary>
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
