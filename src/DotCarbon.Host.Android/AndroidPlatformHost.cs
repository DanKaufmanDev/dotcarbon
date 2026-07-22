using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// Connects CarbonApp to the WebView and lifecycle owned by <see cref="CarbonActivity"/>.
/// </summary>
public sealed class AndroidPlatformHost : ICarbonPlatformHost
{
    private readonly AndroidWebView _mainWebView;

    public AndroidPlatformHost(AndroidWebView mainWebView) => _mainWebView = mainWebView;

    /// <summary>The WebView's Android <see cref="global::Android.Content.Context"/>, for native plugin bindings.</summary>
    public object? NativeHandle => _mainWebView.Native.Context;

    /// <summary>Native Android dialogs, parented to the Activity that owns the WebView.</summary>
    public ICarbonDialogs? Dialogs =>
        _dialogs ??= new AndroidDialogs(() => _mainWebView.Native.Context!);

    private AndroidDialogs? _dialogs;

    public ICarbonWebView CreateWebView(CarbonWebViewContext context)
    {
        if (context.Parent is not null)
            throw new NotSupportedException("Additional windows are not supported on Android.");

        _mainWebView.Attach(context.Callbacks);
        context.Callbacks.Creating?.Invoke();
        return _mainWebView;
    }

    public void Run(ICarbonWebView mainWebView)
    {
        // The Activity owns the platform message loop.
    }
}
