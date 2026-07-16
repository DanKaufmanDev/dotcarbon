using System.Text.Json;
using DotCarbon.Core.Host;
using Foundation;
using WebKit;

namespace DotCarbon.Host.iOS;

/// <summary>
/// Adapts a native <see cref="WKWebView"/> to Carbon's platform-neutral webview contract.
/// </summary>
public sealed class IosWebView : ICarbonWebView
{
    private CarbonWebViewCallbacks? _callbacks;
    private string _title = string.Empty;

    /// <summary>The underlying native WKWebView.</summary>
    public WKWebView Native { get; }

    public IosWebView(WKWebView native) => Native = native;

    internal void Attach(CarbonWebViewCallbacks callbacks) => _callbacks = callbacks;

    /// <summary>Called by the WKScriptMessageHandler when the frontend posts a message.</summary>
    public void DispatchMessage(string message) => _callbacks?.MessageReceived?.Invoke(message);

    internal void RaiseCreated() => _callbacks?.Created?.Invoke();
    internal void RaiseFocused() => _callbacks?.Focused?.Invoke();
    internal void RaiseBlurred() => _callbacks?.Blurred?.Invoke();

    public string Title => _title;
    public int Width => (int)Native.Bounds.Width;
    public int Height => (int)Native.Bounds.Height;
    public int X => 0;
    public int Y => 0;
    public bool IsFullscreen => true;
    public bool IsMaximized => true;
    public bool IsMinimized => false;
    public bool IsAlwaysOnTop => false;
    public bool IsResizable => false;
    // Task 3.1: a mobile app has one window that is always shown and focused while it runs.
    public bool IsVisible => true;
    public bool IsFocused => true;

    public void SetTitle(string title) => _title = title; // no OS title bar on iOS
    public void SetSize(int width, int height) { }
    public void SetPosition(int x, int y) { }
    // Task 3.2: a mobile webview fills the screen, so inner and outer are the same and origin is 0,0.
    public void SetMinSize(int width, int height) { }
    public void SetMaxSize(int width, int height) { }
    public (int, int) GetInnerSize() => (Width, Height);
    public (int, int) GetOuterSize() => (Width, Height);
    public (int, int) GetInnerPosition() => (0, 0);
    public (int, int) GetOuterPosition() => (0, 0);
    public void Center() { }
    public void SetMinimized(bool minimized) { }
    public void SetMaximized(bool maximized) { }
    public void SetFullscreen(bool fullscreen) { }
    public void SetAlwaysOnTop(bool alwaysOnTop) { }
    public void SetResizable(bool resizable) { }
    public void Show() { }
    public void Hide() { }
    public void SetFocus() { }
    public void RequestUserAttention() { }

    public void LoadUri(Uri uri) =>
        Native.InvokeOnMainThread(() => Native.LoadRequest(new NSUrlRequest(new NSUrl(uri.ToString()))));

    public void LoadString(string html) =>
        Native.InvokeOnMainThread(() => Native.LoadHtmlString(new NSString(html), null));

    public Task SendMessageAsync(string message)
    {
        var literal = JsonSerializer.Serialize(message);
        Native.InvokeOnMainThread(() =>
            Native.EvaluateJavaScript(new NSString($"window.__carbonReceive({literal})"), null));
        return Task.CompletedTask;
    }

    public void Close() { } // the app delegate owns the lifetime.
}
