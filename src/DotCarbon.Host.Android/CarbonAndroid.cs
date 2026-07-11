namespace DotCarbon.Host.Android;

/// <summary>Shared constants for the Android bridge transport.</summary>
public static class CarbonAndroid
{
    /// <summary>Name the JS-to-native interface is registered under on the WebView.</summary>
    public const string JsInterfaceName = "CarbonNative";

    /// <summary>
    /// Injected at document start so <c>@dotcarbon/api</c> works unchanged: it expects Photino's
    /// <c>window.external.sendMessage</c> / <c>receiveMessage</c>. Here those map to the Android
    /// JS interface (JS→native) and a receiver the native side calls via evaluateJavascript.
    /// </summary>
    public const string BridgeShim =
        "(function () {" +
        "  var receiver = null;" +
        "  window.external = {" +
        "    sendMessage: function (message) { " + JsInterfaceName + ".postMessage(message); }," +
        "    receiveMessage: function (callback) { receiver = callback; }" +
        "  };" +
        "  window.__carbonReceive = function (message) { if (receiver) { receiver(message); } };" +
        "})();";
}
