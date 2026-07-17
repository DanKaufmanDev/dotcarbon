using System.Text.Json;
using Android.Webkit;
using DotCarbon.Core.Host;

namespace DotCarbon.Host.Android;

/// <summary>
/// The Android <see cref="ICarbonWebView"/>: an adapter over a native <see cref="WebView"/>.
/// Adapts Android's full-screen WebView to Carbon's platform-neutral webview contract.
/// </summary>
public sealed class AndroidWebView : ICarbonWebView
{
    private CarbonWebViewCallbacks? _callbacks;
    private string _title = string.Empty;

    /// <summary>The underlying native Android WebView.</summary>
    public WebView Native { get; }

    public AndroidWebView(WebView native) => Native = native;

    internal void Attach(CarbonWebViewCallbacks callbacks) => _callbacks = callbacks;

    /// <summary>Called by the JS bridge when the frontend posts a message.</summary>
    public void DispatchMessage(string message) => _callbacks?.MessageReceived?.Invoke(message);

    internal void RaiseCreated() => _callbacks?.Created?.Invoke();
    internal void RaiseFocused() => _callbacks?.Focused?.Invoke();
    internal void RaiseBlurred() => _callbacks?.Blurred?.Invoke();

    public string Title => _title;
    public int Width => Native.Width;
    public int Height => Native.Height;
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

    public void SetTitle(string title) => _title = title; // no OS title bar on Android
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
    public void StartDragging() { }
    public void SetDecorations(bool decorations) { }
    public void SetClosable(bool closable) { }
    public void SetMinimizable(bool minimizable) { }
    public void SetMaximizable(bool maximizable) { }
    public void SetAlwaysOnBottom(bool alwaysOnBottom) { }
    public void SetSkipTaskbar(bool skip) { }
    public void SetContentProtected(bool protectedContent) { }
    public void SetIgnoreCursorEvents(bool ignore) { }
    public void SetIcon(string path) { }
    public void SetCursorIcon(string icon) { }
    public void SetCursorVisible(bool visible) { }
    public void SetCursorGrab(bool grab) { }
    public void SetCursorPosition(int x, int y) { }
    // Task 3.5: a mobile device is a single screen.
    public IReadOnlyList<CarbonMonitorInfo> GetMonitors() => [GetPrimaryMonitor()!];
    public CarbonMonitorInfo? GetPrimaryMonitor() => new(null, 0, 0, Width, Height, 0, 0, Width, Height, 1.0);
    public CarbonMonitorInfo? GetCurrentMonitor() => GetPrimaryMonitor();
    public double GetScaleFactor() => 1.0;
    public string GetTheme() => "light";
    public void SetTheme(string theme) { }
    public void SetProgressBar(string status, int progress) { }
    public void SetBadge(string? label) { }

    public void LoadUri(Uri uri) => Native.Post(() => Native.LoadUrl(uri.ToString()));

    public void LoadString(string html) =>
        Native.Post(() => Native.LoadData(html, "text/html", "utf-8"));

    public Task SendMessageAsync(string message)
    {
        // WebView evaluation must run on the Android UI thread.
        var literal = JsonSerializer.Serialize(message);
        Native.Post(() => Native.EvaluateJavascript($"window.__carbonReceive({literal})", null));
        return Task.CompletedTask;
    }

    public void Close() { } // the Activity owns the lifetime; finishing it is up to the app.
}
