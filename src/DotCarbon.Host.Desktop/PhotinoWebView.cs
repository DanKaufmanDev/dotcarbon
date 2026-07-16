using System.Drawing;
using DotCarbon.Core.Host;
using Photino.NET;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// The desktop <see cref="ICarbonWebView"/>: a thin adapter over a <see cref="PhotinoWindow"/>.
/// All Photino-specific window/webview behavior lives here, keeping DotCarbon.Core platform-free.
/// </summary>
public sealed class PhotinoWebView : ICarbonWebView
{
    /// <summary>The underlying Photino window (for desktop-only plugins that need native access).</summary>
    public PhotinoWindow Window { get; }

    /// <summary>
    /// The native window handle (NSWindow / GtkWindow) resolved for the show/hide/focus operations
    /// Photino does not provide. Cached once so a later retitle cannot lose it; unused on Windows,
    /// where the HWND comes straight from Photino. See <see cref="NativeWindowControls"/>.
    /// </summary>
    internal IntPtr NativeWindow { get; set; }

    private readonly string _titleBarStyle;
    private readonly int _requestedWidth;
    private readonly int _requestedHeight;

    internal PhotinoWebView(CarbonWebViewContext context)
    {
        var options = context.Options;
        var callbacks = context.Callbacks;
        var parent = (context.Parent as PhotinoWebView)?.Window;
        _titleBarStyle = options.TitleBarStyle;
        (_requestedWidth, _requestedHeight) = (options.Width, options.Height);

        Window = new PhotinoWindow(parent)
            .SetTitle(options.Title)
            .SetSize(options.Width, options.Height)
            .SetResizable(options.Resizable)
            .SetChromeless(!options.Decorations)
            .SetTransparent(options.Transparent)
            .SetTopMost(options.AlwaysOnTop)
            .SetMaximized(options.Maximized)
            .SetFullScreen(options.Fullscreen)
            .SetDevToolsEnabled(options.DevTools)
            .SetContextMenuEnabled(options.ContextMenu)
            .RegisterCustomSchemeHandler("carbon", ServeCarbonAsset)
            .RegisterWindowCreatingHandler((_, _) => callbacks.Creating?.Invoke())
            .RegisterWindowCreatedHandler((_, _) =>
            {
                // The native window exists now, so the title-bar style (Task 3.2 full-window mode)
                // can be applied. Runs on Photino's UI thread, where AppKit calls are valid.
                // Making the title bar transparent shrinks the frame to the old content height (AppKit
                // folds the title bar into the content), so the requested size is re-applied — the
                // window stays as asked and the webview fills all of it.
                // Window is assigned by the time this fires (it runs after the app starts), which the
                // compiler can't see from inside the constructor's own initializer chain.
                if (NativeWindowControls.SetTitleBarStyle(this, _titleBarStyle))
                    Window!.SetSize(_requestedWidth, _requestedHeight);
                callbacks.Created?.Invoke();
            })
            .RegisterWindowClosingHandler((_, _) => callbacks.Closing?.Invoke() ?? false)
            .RegisterFocusInHandler((_, _) => callbacks.Focused?.Invoke())
            .RegisterFocusOutHandler((_, _) => callbacks.Blurred?.Invoke())
            .RegisterLocationChangedHandler((_, point) => callbacks.Moved?.Invoke(point.X, point.Y))
            .RegisterSizeChangedHandler((_, size) => callbacks.Resized?.Invoke(size.Width, size.Height))
            .RegisterMinimizedHandler((_, _) => callbacks.Minimized?.Invoke())
            .RegisterMaximizedHandler((_, _) => callbacks.Maximized?.Invoke())
            .RegisterRestoredHandler((_, _) => callbacks.Restored?.Invoke())
            .RegisterWebMessageReceivedHandler((_, message) => callbacks.MessageReceived?.Invoke(message));

        if (options.MinWidth is int minWidth) Window.SetMinWidth(minWidth);
        if (options.MinHeight is int minHeight) Window.SetMinHeight(minHeight);
        if (options.MaxWidth is int maxWidth) Window.SetMaxWidth(maxWidth);
        if (options.MaxHeight is int maxHeight) Window.SetMaxHeight(maxHeight);

        if (!string.IsNullOrWhiteSpace(options.Icon))
        {
            var icon = Path.GetFullPath(options.Icon);
            if (File.Exists(icon)) Window.SetIconFile(icon);
        }

        if (options.X is int x && options.Y is int y)
            Window.SetLeft(x).SetTop(y);
        else if (options.Center)
            Window.Center();
    }

    public string Title => Window.Title;
    public int Width => Window.Size.Width;
    public int Height => Window.Size.Height;
    public int X => Window.Location.X;
    public int Y => Window.Location.Y;
    public bool IsFullscreen => Window.FullScreen;
    public bool IsMaximized => Window.Maximized;
    public bool IsMinimized => Window.Minimized;
    public bool IsAlwaysOnTop => Window.Topmost;
    public bool IsResizable => Window.Resizable;
    public bool IsVisible => NativeWindowControls.IsVisible(this);
    public bool IsFocused => NativeWindowControls.IsFocused(this);

    public void SetTitle(string title) => Window.SetTitle(title);
    public void SetSize(int width, int height) => Window.SetSize(width, height);
    public void SetPosition(int x, int y) => Window.SetLocation(new Point(x, y));
    public void Center() => Window.Center();
    // Photino's SetMinSize/SetMaxSize convenience methods are no-ops (they never update the window's
    // MinWidth/MinHeight), so the per-dimension setters, which do work, are used instead.
    public void SetMinSize(int width, int height)
    {
        Window.SetMinWidth(width);
        Window.SetMinHeight(height);
    }

    public void SetMaxSize(int width, int height)
    {
        Window.SetMaxWidth(width);
        Window.SetMaxHeight(height);
    }
    public (int, int) GetInnerSize() => NativeWindowControls.InnerSize(this);
    public (int, int) GetOuterSize() => NativeWindowControls.OuterSize(this);
    public (int, int) GetInnerPosition() => NativeWindowControls.InnerPosition(this);
    public (int, int) GetOuterPosition() => NativeWindowControls.OuterPosition(this);
    public void SetMinimized(bool minimized) => Window.SetMinimized(minimized);
    public void SetMaximized(bool maximized) => Window.SetMaximized(maximized);
    public void SetFullscreen(bool fullscreen) => Window.SetFullScreen(fullscreen);
    public void SetAlwaysOnTop(bool alwaysOnTop) => Window.SetTopMost(alwaysOnTop);
    public void SetResizable(bool resizable) => Window.SetResizable(resizable);

    /// <summary>
    /// Apply a title-bar style at runtime (config `window.titleBarStyle` uses the same call at
    /// creation). "transparent" makes the webview fill the whole window on macOS.
    /// </summary>
    public void SetTitleBarStyle(string style)
    {
        // Capture the size first: applying a transparent title bar shrinks the frame to the old
        // content height, so it must be restored to what it was, not what it becomes.
        var (width, height) = (Width, Height);
        if (NativeWindowControls.SetTitleBarStyle(this, style))
            Window.SetSize(width, height);
    }

    // Task 3.1 — native show/hide/focus/attention (Photino provides none of these).
    public void Show() => NativeWindowControls.Show(this);
    public void Hide() => NativeWindowControls.Hide(this);
    public void SetFocus() => NativeWindowControls.SetFocus(this);
    public void RequestUserAttention() => NativeWindowControls.RequestUserAttention(this);
    public void StartDragging() => NativeWindowControls.StartDragging(this);

    // Task 3.3 — chrome & behavior (Photino exposes almost none of these at runtime).
    public void SetDecorations(bool decorations) => NativeWindowControls.SetDecorations(this, decorations);
    public void SetClosable(bool closable) => NativeWindowControls.SetClosable(this, closable);
    public void SetMinimizable(bool minimizable) => NativeWindowControls.SetMinimizable(this, minimizable);
    public void SetMaximizable(bool maximizable) => NativeWindowControls.SetMaximizable(this, maximizable);
    public void SetAlwaysOnBottom(bool alwaysOnBottom) => NativeWindowControls.SetAlwaysOnBottom(this, alwaysOnBottom);
    public void SetSkipTaskbar(bool skip) => NativeWindowControls.SetSkipTaskbar(this, skip);
    public void SetContentProtected(bool protectedContent) => NativeWindowControls.SetContentProtected(this, protectedContent);
    public void SetIgnoreCursorEvents(bool ignore) => NativeWindowControls.SetIgnoreCursorEvents(this, ignore);

    public void SetIcon(string path)
    {
        // Photino's icon setter handles the Windows/Linux taskbar icon; macOS has no title-bar icon.
        var full = Path.GetFullPath(path);
        if (File.Exists(full)) Window.SetIconFile(full);
    }

    // Task 3.5 — monitors (all from Photino's Monitor data; no native code needed).
    public IReadOnlyList<CarbonMonitorInfo> GetMonitors() =>
        Window.Monitors.Select(ToMonitorInfo).ToList();

    public CarbonMonitorInfo? GetPrimaryMonitor() => ToMonitorInfo(Window.MainMonitor);

    public CarbonMonitorInfo? GetCurrentMonitor()
    {
        // The display whose bounds contain the window's centre point.
        var cx = X + Width / 2;
        var cy = Y + Height / 2;
        foreach (var monitor in Window.Monitors)
        {
            var area = monitor.MonitorArea;
            if (cx >= area.X && cx < area.X + area.Width && cy >= area.Y && cy < area.Y + area.Height)
                return ToMonitorInfo(monitor);
        }
        return ToMonitorInfo(Window.MainMonitor);
    }

    public double GetScaleFactor() => GetCurrentMonitor()?.ScaleFactor ?? Window.MainMonitor.Scale;

    private static CarbonMonitorInfo ToMonitorInfo(Photino.NET.Monitor monitor) => new(
        Name: null,
        monitor.MonitorArea.X, monitor.MonitorArea.Y, monitor.MonitorArea.Width, monitor.MonitorArea.Height,
        monitor.WorkArea.X, monitor.WorkArea.Y, monitor.WorkArea.Width, monitor.WorkArea.Height,
        monitor.Scale);

    // Task 3.4 — cursor.
    public void SetCursorIcon(string icon) => NativeWindowControls.SetCursorIcon(this, icon);
    public void SetCursorVisible(bool visible) => NativeWindowControls.SetCursorVisible(this, visible);
    public void SetCursorGrab(bool grab) => NativeWindowControls.SetCursorGrab(this, grab);
    public void SetCursorPosition(int x, int y) => NativeWindowControls.SetCursorPosition(this, x, y);

    /// <summary>macOS: the cursor's current screen position (top-left origin), for verification.</summary>
    public (int X, int Y) MacGlobalCursor() => NativeWindowControls.MacGlobalCursor();

    /// <summary>macOS: whether a style-mask bit ("titled"/"closable"/"miniaturizable") is set.</summary>
    public bool MacHasStyleBit(string which) => NativeWindowControls.MacHasStyleBit(this, which);
    /// <summary>macOS: whether the window is excluded from screen capture.</summary>
    public bool MacIsContentProtected() => NativeWindowControls.MacIsContentProtected(this);
    /// <summary>macOS: whether the window passes pointer events through (click-through).</summary>
    public bool MacIgnoresCursor() => NativeWindowControls.MacIgnoresCursor(this);

    public void LoadUri(Uri uri) => Window.Load(uri);
    public void LoadString(string html) => Window.LoadRawString(html);
    public Task SendMessageAsync(string message) => Window.SendWebMessageAsync(message);
    public void Close() => Window.Close();

    private static Stream ServeCarbonAsset(object sender, string scheme, string url, out string contentType)
    {
        var response = CarbonAssets.Serve(url);
        contentType = response.ContentType;
        return response.Content;
    }
}
