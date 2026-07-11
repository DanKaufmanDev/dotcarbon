namespace DotCarbon.Core.Host;

/// <summary>
/// A platform-agnostic webview surface the Carbon runtime drives. Desktop (Photino),
/// Android (Android WebView) and iOS (WKWebView) each provide an implementation; the
/// runtime — command dispatch, capabilities, events, lifecycle — never touches a
/// concrete webview. This is the seam described in the bundler roadmap's Core/Host split.
/// </summary>
public interface ICarbonWebView
{
    string Title { get; }
    int Width { get; }
    int Height { get; }
    int X { get; }
    int Y { get; }
    bool IsFullscreen { get; }
    bool IsMaximized { get; }
    bool IsMinimized { get; }
    bool IsAlwaysOnTop { get; }
    bool IsResizable { get; }

    void SetTitle(string title);
    void SetSize(int width, int height);
    void SetPosition(int x, int y);
    void Center();
    void SetMinimized(bool minimized);
    void SetMaximized(bool maximized);
    void SetFullscreen(bool fullscreen);
    void SetAlwaysOnTop(bool alwaysOnTop);
    void SetResizable(bool resizable);

    /// <summary>Navigate the webview to a URI (carbon://, http://localhost dev server, or an external URL).</summary>
    void LoadUri(Uri uri);

    /// <summary>Load a raw HTML string (used for the no-frontend fallback screen).</summary>
    void LoadString(string html);

    /// <summary>Push a bridge message (command response or event) to the JavaScript side.</summary>
    Task SendMessageAsync(string message);

    void Close();
}
