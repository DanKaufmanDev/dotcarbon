using Android.App;
using Android.OS;
using Android.Webkit;
using AndroidX.Webkit;
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
        native.SetWebViewClient(new CarbonWebViewClient());

        var webView = _webView = new AndroidWebView(native);
        native.AddJavascriptInterface(new CarbonJsBridge(webView.DispatchMessage), CarbonAndroid.JsInterfaceName);
        if (WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
            WebViewCompat.AddDocumentStartJavaScript(native, CarbonAndroid.BridgeShim, new HashSet<string> { "*" });

        SetContentView(native);

        var app = _app = CarbonApp.Create(LoadConfig()).UsePlatform(new AndroidPlatformHost(webView));
        ConfigureApp(app);
        app.Start();
        webView.RaiseCreated();
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
