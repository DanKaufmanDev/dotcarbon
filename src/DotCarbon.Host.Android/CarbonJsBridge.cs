using Android.Webkit;
using Java.Interop;

namespace DotCarbon.Host.Android;

/// <summary>
/// Receives frontend bridge messages through the WebView's <c>CarbonNative</c> interface.
/// </summary>
public sealed class CarbonJsBridge : Java.Lang.Object
{
    private readonly Action<string> _onMessage;

    public CarbonJsBridge(Action<string> onMessage) => _onMessage = onMessage;

    [JavascriptInterface]
    [Export("postMessage")]
    public void PostMessage(string message) => _onMessage(message);
}
