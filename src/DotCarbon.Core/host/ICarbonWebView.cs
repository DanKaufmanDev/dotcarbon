namespace DotCarbon.Core.Host;

/// <summary>
/// Platform-neutral webview operations used by the runtime. Each host adapts these operations to
/// its native webview without leaking platform types into Core.
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

    /// <summary>Whether the window is shown (not hidden with <see cref="Hide"/>). Task 3.1.</summary>
    bool IsVisible { get; }

    /// <summary>Whether the window currently has keyboard focus. Task 3.1.</summary>
    bool IsFocused { get; }

    void SetTitle(string title);
    void SetSize(int width, int height);
    void SetPosition(int x, int y);
    void Center();
    void SetMinimized(bool minimized);
    void SetMaximized(bool maximized);
    void SetFullscreen(bool fullscreen);
    void SetAlwaysOnTop(bool alwaysOnTop);
    void SetResizable(bool resizable);

    /// <summary>Show a window hidden with <see cref="Hide"/>, and bring it forward. Task 3.1.</summary>
    void Show();

    /// <summary>Hide the window entirely — off the taskbar/dock, not merely minimized. Task 3.1.</summary>
    void Hide();

    /// <summary>Bring the window to the front and give it keyboard focus. Task 3.1.</summary>
    void SetFocus();

    /// <summary>
    /// Ask the OS to draw attention to the window (taskbar flash / bouncing dock icon) when it is not
    /// focused. Task 3.1.
    /// </summary>
    void RequestUserAttention();

    /// <summary>Navigate the webview to a URI (carbon://, http://localhost dev server, or an external URL).</summary>
    void LoadUri(Uri uri);

    /// <summary>Load a raw HTML string (used for the no-frontend fallback screen).</summary>
    void LoadString(string html);

    /// <summary>Push a bridge message (command response or event) to the JavaScript side.</summary>
    Task SendMessageAsync(string message);

    void Close();
}
