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

        // A controller that republishes the safe-area insets whenever they change (rotation, notch).
        var controller = new SafeAreaViewController(() => PublishSafeAreaInsets(native)) { View = native };
        Window = new UIWindow(UIScreen.MainScreen.Bounds) { RootViewController = controller };
        Window.MakeKeyAndVisible();

        // A deep link that launched the app arrives in the launch options; record it before Start()
        // so the DeepLink plugin sees it at initialization.
        if (launchOptions?[UIApplication.LaunchOptionsUrlKey] is NSUrl launchUrl)
            CarbonDeepLinks.Deliver(launchUrl.AbsoluteString);

        var app = _app = CarbonApp.Create(LoadConfig()).UsePlatform(new IosPlatformHost(webView));
        ConfigureApp(app);
        app.Start();
        webView.RaiseCreated();
        return true;
    }

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        // A deep link received while the app is already running.
        CarbonDeepLinks.Deliver(url.AbsoluteString);
        return true;
    }

    /// <summary>
    /// Publishes the safe-area insets to CSS as <c>--carbon-safe-area-{top,right,bottom,left}</c>. iOS
    /// also offers <c>env(safe-area-inset-*)</c>, but only when the page opts in with
    /// <c>viewport-fit=cover</c>; these custom properties work the same way on Android too, so a layout
    /// can use one API on both platforms. Insets are already in points, which map 1:1 to CSS pixels.
    /// </summary>
    private static void PublishSafeAreaInsets(WKWebView webView)
    {
        var insets = webView.SafeAreaInsets;
        var script =
            "(function(s){" +
            $"s.setProperty('--carbon-safe-area-top','{insets.Top:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-right','{insets.Right:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-bottom','{insets.Bottom:0.##}px');" +
            $"s.setProperty('--carbon-safe-area-left','{insets.Left:0.##}px');" +
            "})(document.documentElement.style)";
        webView.EvaluateJavaScript(new NSString(script), null);
    }

    private sealed class SafeAreaViewController : UIViewController
    {
        private readonly Action _onSafeAreaChanged;

        public SafeAreaViewController(Action onSafeAreaChanged) => _onSafeAreaChanged = onSafeAreaChanged;

        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();
            _onSafeAreaChanged();
        }
    }

    public override void OnActivated(UIApplication application) => _webView?.RaiseFocused();

    public override void OnResignActivation(UIApplication application) => _webView?.RaiseBlurred();

    public override void WillTerminate(UIApplication application) => _app?.Shutdown();
}
