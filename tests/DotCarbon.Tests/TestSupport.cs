using DotCarbon.Core.Host;

namespace DotCarbon.Tests;

/// <summary>A no-op platform host + webview for exercising the runtime without a real window.</summary>
internal sealed class NoopHost : ICarbonPlatformHost
{
    public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new NoopWebView();
    public void Run(ICarbonWebView mainWebView) { }
}

/// <summary>
/// A host that keeps each window's webview by label so a test can see which windows an event reached.
/// </summary>
internal sealed class RecordingHost : ICarbonPlatformHost
{
    public Dictionary<string, NoopWebView> Views { get; } = new(StringComparer.Ordinal);

    public ICarbonWebView CreateWebView(CarbonWebViewContext context)
    {
        var view = new NoopWebView();
        Views[context.Options.Label] = view;
        return view;
    }

    public void Run(ICarbonWebView mainWebView) { }
}

internal sealed class NoopWebView : ICarbonWebView
{
    /// <summary>Messages pushed to this webview (bridge responses, events, channel frames).</summary>
    public List<string> Sent { get; } = [];

    public string Title => "main";
    public int Width => 800; public int Height => 600; public int X => 0; public int Y => 0;
    public bool IsFullscreen => false; public bool IsMaximized => false; public bool IsMinimized => false;
    public bool IsAlwaysOnTop => false; public bool IsResizable => true; public bool IsVisible => true;
    public bool IsFocused => false;
    /// <summary>Records the last geometry setter calls so tests can observe window-state restore.</summary>
    public (int Width, int Height)? LastSize { get; private set; }
    public (int X, int Y)? LastPosition { get; private set; }
    public bool? LastMaximized { get; private set; }

    public void SetTitle(string t) { } public void SetSize(int w, int h) { LastSize = (w, h); } public void SetPosition(int x, int y) { LastPosition = (x, y); }
    public void Center() { } public void SetMinSize(int w, int h) { } public void SetMaxSize(int w, int h) { }
    public (int, int) GetInnerSize() => (800, 600); public (int, int) GetOuterSize() => (800, 600);
    public (int, int) GetInnerPosition() => (0, 0); public (int, int) GetOuterPosition() => (0, 0);
    public void SetMinimized(bool m) { } public void SetMaximized(bool m) { LastMaximized = m; } public void SetFullscreen(bool f) { }
    public void SetAlwaysOnTop(bool a) { } public void SetResizable(bool r) { }
    public void Show() { } public void Hide() { } public void SetFocus() { } public void RequestUserAttention() { }
    public void StartDragging() { }
    public void SetDecorations(bool d) { } public void SetClosable(bool c) { } public void SetMinimizable(bool m) { }
    public void SetMaximizable(bool m) { } public void SetAlwaysOnBottom(bool a) { } public void SetSkipTaskbar(bool s) { }
    public void SetContentProtected(bool p) { } public void SetIgnoreCursorEvents(bool i) { } public void SetIcon(string p) { }
    public void SetCursorIcon(string i) { } public void SetCursorVisible(bool v) { } public void SetCursorGrab(bool g) { }
    public void SetCursorPosition(int x, int y) { }
    public IReadOnlyList<CarbonMonitorInfo> GetMonitors() => [GetPrimaryMonitor()!];
    public CarbonMonitorInfo? GetPrimaryMonitor() => new(null, 0, 0, 800, 600, 0, 0, 800, 600, 1.0);
    public CarbonMonitorInfo? GetCurrentMonitor() => GetPrimaryMonitor();
    public double GetScaleFactor() => 1.0;
    public string GetTheme() => "light"; public void SetTheme(string t) { }
    public void SetProgressBar(string status, int progress) { } public void SetBadge(string? label) { }
    public void SetEffect(string effect) { }
    public void LoadUri(System.Uri uri) { } public void LoadString(string html) { }
    public System.Threading.Tasks.Task SendMessageAsync(string message)
    {
        Sent.Add(message);
        return System.Threading.Tasks.Task.CompletedTask;
    }
    public void Close() { }
}
