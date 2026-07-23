using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Android;

/// <summary>
/// Base Activity that hosts a Carbon app in an Android WebView. A generated app's
/// <c>MainActivity</c> subclasses this and overrides <see cref="ConfigureApp"/> to register its
/// backend plugins (the same <c>src-carbon</c> commands used on desktop).
/// </summary>
public abstract class CarbonActivity : Activity
{
    private CarbonApp? _app;
    private AndroidWebView? _webView;
    private (int Left, int Top, int Right, int Bottom) _safeArea;

    /// <summary>Register backend plugins (e.g. <c>app.WithPlugin&lt;AppCommands&gt;()</c>).</summary>
    protected virtual void ConfigureApp(CarbonApp app) { }

    /// <summary>Load configuration. Defaults to the embedded <c>carbon.json</c>.</summary>
    protected virtual CarbonConfig LoadConfig() => ConfigLoader.Load();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var native = new WebView(this);
        native.Settings.JavaScriptEnabled = true;
        native.Settings.DomStorageEnabled = true;
        native.SetWebViewClient(new CarbonWebViewClient(PublishSafeAreaInsets));
        native.SetWebChromeClient(new CarbonWebChromeClient());

        var webView = _webView = new AndroidWebView(native);
        native.AddJavascriptInterface(new CarbonJsBridge(webView.DispatchMessage), CarbonAndroid.JsInterfaceName);
        // CarbonWebViewClient installs the bridge when navigation begins.

        SetContentView(native);
        GoEdgeToEdge();
        ApplySafeAreaInsets(native);

        // A deep link that launched the app arrives as the intent's data URI; record it before Start()
        // so the DeepLink plugin sees it at initialization.
        CarbonDeepLinks.Deliver(Intent?.DataString);

        var app = _app = CarbonApp.Create(LoadConfig()).UsePlatform(new AndroidPlatformHost(webView));
        ConfigureApp(app);
        app.Start();
        webView.RaiseCreated();
    }

    /// <summary>
    /// Lets the webview draw behind the status and navigation bars. Without this the system insets the
    /// content itself and the safe-area values are all zero — useless to a layout. Android 15 makes
    /// edge-to-edge mandatory anyway, so this is the convention rather than an opt-in.
    /// </summary>
    private void GoEdgeToEdge()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            Window?.SetDecorFitsSystemWindows(false);
    }

    /// <summary>
    /// Publishes the system-bar / display-cutout insets to CSS as
    /// <c>--carbon-safe-area-{top,right,bottom,left}</c>, so layouts can avoid the notch and gesture bar
    /// with the same custom properties on both platforms (iOS's <c>env(safe-area-inset-*)</c> is only
    /// available with <c>viewport-fit=cover</c>, and Android's WebView has no equivalent at all).
    /// </summary>
    private void ApplySafeAreaInsets(WebView webView)
    {
        webView.SetOnApplyWindowInsetsListener(new SafeAreaInsetsListener((left, top, right, bottom) =>
        {
            _safeArea = (left, top, right, bottom);
            PublishSafeAreaInsets();
        }));
    }

    /// <summary>
    /// Writes the cached insets into the current document. Called both when the insets change and when a
    /// page finishes loading — a navigation replaces the document, discarding anything set before it.
    /// </summary>
    private void PublishSafeAreaInsets()
    {
        var native = _webView?.Native;
        if (native is null) return;

        // CSS pixels: Android insets are physical pixels, so divide by the display density.
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0) density = 1f;

        var (left, top, right, bottom) = _safeArea;
        var script =
            // This also runs at page-start, where the document element may not exist yet: apply as soon
            // as it does, so frontend code reading the values at boot sees them rather than empty strings.
            "(function(){var apply=function(){var e=document.documentElement;if(!e)return false;" +
            "var s=e.style;" +
            $"s.setProperty('--carbon-safe-area-top','{top / density:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-right','{right / density:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-bottom','{bottom / density:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-left','{left / density:0.##}px');" +
            "return true;};" +
            "if(!apply())document.addEventListener('DOMContentLoaded',apply);})()";
        native.Post(() => native.EvaluateJavascript(script, null));
    }

    /// <summary>
    /// Android's back gesture/button. The frontend is told about it, and the default behaviour is the
    /// one users expect: step back through webview history, or leave the app when there is none.
    /// </summary>
    public override void OnBackPressed()
    {
        var emit = _app?.Handle.EmitAsync(
            new CarbonEventName<string>("carbon://back"), "back", CarbonHostJsonContext.Default.String);

        if (emit is null)
        {
            NavigateBack();
            return;
        }

        // Navigate only once the event has been dispatched: going back tears down the JS context, so
        // doing both at once loses the event the frontend was told it would get.
        emit.ContinueWith(_ => RunOnUiThread(NavigateBack), TaskScheduler.Default);
    }

    private void NavigateBack()
    {
        if (_webView?.Native.CanGoBack() == true)
        {
            _webView.Native.GoBack();
            return;
        }

#pragma warning disable CA1422 // the predictive-back API is opt-in; this is the default path
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    private sealed class SafeAreaInsetsListener : Java.Lang.Object, global::Android.Views.View.IOnApplyWindowInsetsListener
    {
        private readonly Action<int, int, int, int> _onInsets;

        public SafeAreaInsetsListener(Action<int, int, int, int> onInsets) => _onInsets = onInsets;

        public WindowInsets OnApplyWindowInsets(global::Android.Views.View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(
                    WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                _onInsets(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            else
            {
#pragma warning disable CA1422, CS0618 // pre-API-30 inset accessors
                _onInsets(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop,
                    insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
#pragma warning restore CA1422, CS0618
            }
            return insets;
        }
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        // Resolve whichever AndroidPermissions.RequestAsync call is awaiting this request code.
        AndroidPermissions.Complete(requestCode, grantResults);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        // A deep link received while the app is already running.
        CarbonDeepLinks.Deliver(intent?.DataString);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _webView?.RaiseFocused();
    }

    protected override void OnPause()
    {
        _webView?.RaiseBlurred();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        _app?.Shutdown();
        base.OnDestroy();
    }
}
