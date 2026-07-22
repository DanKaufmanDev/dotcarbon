using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Webkit;
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
        native.SetWebChromeClient(new CarbonWebChromeClient());

        var webView = _webView = new AndroidWebView(native);
        native.AddJavascriptInterface(new CarbonJsBridge(webView.DispatchMessage), CarbonAndroid.JsInterfaceName);
        // CarbonWebViewClient installs the bridge when navigation begins.

        SetContentView(native);

        // A deep link that launched the app arrives as the intent's data URI; record it before Start()
        // so the DeepLink plugin sees it at initialization.
        CarbonDeepLinks.Deliver(Intent?.DataString);

        var app = _app = CarbonApp.Create(LoadConfig()).UsePlatform(new AndroidPlatformHost(webView));
        ConfigureApp(app);
        app.Start();
        webView.RaiseCreated();
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
