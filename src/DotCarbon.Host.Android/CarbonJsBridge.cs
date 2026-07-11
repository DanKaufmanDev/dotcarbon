using Android.Webkit;
using Java.Interop;

namespace DotCarbon.Host.Android;

/// <summary>
/// The JS→native side of the bridge. Registered on the WebView as <c>CarbonNative</c>; the shim
/// in <see cref="CarbonAndroid.BridgeShim"/> routes <c>window.external.sendMessage</c> here.
/// </summary>
public sealed class CarbonJsBridge : Java.Lang.Object
{
    private readonly Action<string> _onMessage;

    public CarbonJsBridge(Action<string> onMessage) => _onMessage = onMessage;

    [JavascriptInterface]
    [Export("postMessage")]
    public void PostMessage(string message) => _onMessage(message);
}
