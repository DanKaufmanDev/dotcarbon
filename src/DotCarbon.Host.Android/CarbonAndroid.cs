namespace DotCarbon.Host.Android;

/// <summary>Shared constants for the Android bridge transport.</summary>
public static class CarbonAndroid
{
    /// <summary>Name the JS-to-native interface is registered under on the WebView.</summary>
    public const string JsInterfaceName = "CarbonNative";

    /// <summary>
    /// Installs the bridge shape expected by <c>@dotcarbon/api</c> over Android's JavaScript interface.
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
