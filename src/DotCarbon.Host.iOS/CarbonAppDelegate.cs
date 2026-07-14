using CoreGraphics;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using Foundation;
using UIKit;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Base UIApplicationDelegate that hosts a Carbon app in a WKWebView. A generated app's
/// <c>AppDelegate</c> subclasses this and overrides <see cref="ConfigureApp"/> to register its
/// backend plugins (the same <c>src-carbon</c> commands used on desktop).
/// </summary>
public abstract class CarbonAppDelegate : UIApplicationDelegate
{
    private CarbonApp? _app;
    private IosWebView? _webView;

    public override UIWindow? Window { get; set; }

    /// <summary>Register backend plugins (e.g. <c>app.WithPlugin&lt;AppCommands&gt;()</c>).</summary>
    protected virtual void ConfigureApp(CarbonApp app) { }

    /// <summary>Load configuration. Defaults to the embedded <c>carbon.json</c>.</summary>
    protected virtual CarbonConfig LoadConfig() => ConfigLoader.Load();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var configuration = new WKWebViewConfiguration();
        configuration.SetUrlSchemeHandler(new CarbonSchemeHandler(), "carbon");
        configuration.UserContentController.AddUserScript(
            new WKUserScript(new NSString(CarbonIos.BridgeShim), WKUserScriptInjectionTime.AtDocumentStart, true));
        configuration.UserContentController.AddUserScript(
            new WKUserScript(new NSString(CarbonIos.ConsoleShim), WKUserScriptInjectionTime.AtDocumentStart, true));
        configuration.UserContentController.AddScriptMessageHandler(
            new CarbonScriptMessageHandler(message => _webView?.DispatchMessage(message)), CarbonIos.MessageHandlerName);
        configuration.UserContentController.AddScriptMessageHandler(
            new CarbonConsoleMessageHandler(), CarbonIos.ConsoleHandlerName);

        var native = new WKWebView(UIScreen.MainScreen.Bounds, configuration)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
        };
        var webView = _webView = new IosWebView(native);

        var controller = new UIViewController { View = native };
        Window = new UIWindow(UIScreen.MainScreen.Bounds) { RootViewController = controller };
        Window.MakeKeyAndVisible();

        var app = _app = CarbonApp.Create(LoadConfig()).UsePlatform(new IosPlatformHost(webView));
        ConfigureApp(app);
        app.Start();
        webView.RaiseCreated();
        return true;
    }

    public override void OnActivated(UIApplication application) => _webView?.RaiseFocused();

    public override void OnResignActivation(UIApplication application) => _webView?.RaiseBlurred();

    public override void WillTerminate(UIApplication application) => _app?.Shutdown();
}
