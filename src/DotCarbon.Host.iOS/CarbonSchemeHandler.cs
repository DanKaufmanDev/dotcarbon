using DotCarbon.Core.Host;
using Foundation;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Serves <c>carbon://localhost/…</c> requests from the embedded frontend, reusing Core's
/// <see cref="CarbonAssets"/> (path safety, SPA fallback, CSP) — the same content pipeline as desktop.
/// Registered on the WKWebViewConfiguration for the <c>carbon</c> scheme.
/// </summary>
public sealed class CarbonSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var requestUrl = urlSchemeTask.Request.Url;
        var url = requestUrl?.AbsoluteString ?? string.Empty;

        var served = CarbonAssets.Serve(url);
        using var buffer = new MemoryStream();
        served.Content.CopyTo(buffer);
        var data = NSData.FromArray(buffer.ToArray());

        var response = new NSUrlResponse(requestUrl!, MimeType(served.ContentType), (nint)data.Length, "utf-8");
        urlSchemeTask.DidReceiveResponse(response);
        urlSchemeTask.DidReceiveData(data);
        urlSchemeTask.DidFinish();
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }

    private static string MimeType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return separator >= 0 ? contentType[..separator].Trim() : contentType.Trim();
    }
}
