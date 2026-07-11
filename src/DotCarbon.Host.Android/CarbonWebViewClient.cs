using Android.Webkit;
using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// Serves <c>carbon://localhost/…</c> requests from the embedded frontend, reusing Core's
/// <see cref="CarbonAssets"/> (path safety, SPA fallback, CSP) — the same content pipeline as desktop.
/// </summary>
public sealed class CarbonWebViewClient : WebViewClient
{
    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is not null && url.StartsWith("carbon://", StringComparison.OrdinalIgnoreCase))
        {
            var response = CarbonAssets.Serve(url);
            return new WebResourceResponse(MimeType(response.ContentType), "utf-8", response.Content);
        }
        return base.ShouldInterceptRequest(view, request);
    }

    private static string MimeType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return separator >= 0 ? contentType[..separator].Trim() : contentType.Trim();
    }
}
