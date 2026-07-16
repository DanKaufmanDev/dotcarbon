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

    internal PhotinoWebView(CarbonWebViewContext context)
    {
        var options = context.Options;
        var callbacks = context.Callbacks;
        var parent = (context.Parent as PhotinoWebView)?.Window;

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
            .RegisterWindowCreatedHandler((_, _) => callbacks.Created?.Invoke())
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
    public void SetMinimized(bool minimized) => Window.SetMinimized(minimized);
    public void SetMaximized(bool maximized) => Window.SetMaximized(maximized);
    public void SetFullscreen(bool fullscreen) => Window.SetFullScreen(fullscreen);
    public void SetAlwaysOnTop(bool alwaysOnTop) => Window.SetTopMost(alwaysOnTop);
    public void SetResizable(bool resizable) => Window.SetResizable(resizable);

    // Task 3.1 — native show/hide/focus/attention (Photino provides none of these).
    public void Show() => NativeWindowControls.Show(this);
    public void Hide() => NativeWindowControls.Hide(this);
    public void SetFocus() => NativeWindowControls.SetFocus(this);
    public void RequestUserAttention() => NativeWindowControls.RequestUserAttention(this);

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
