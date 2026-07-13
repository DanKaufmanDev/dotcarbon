using Android.Util;
using Android.Webkit;

namespace DotCarbon.Host.Android;

/// <summary>Forwards WebView console messages into logcat so CI and users can inspect frontend boot.</summary>
public sealed class CarbonWebChromeClient : WebChromeClient
{
    private const string Tag = "DotCarbon";

    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
    {
        var message = consoleMessage?.Message();
        if (!string.IsNullOrWhiteSpace(message))
            Log.Info(Tag, message);

        return base.OnConsoleMessage(consoleMessage);
    }
}
