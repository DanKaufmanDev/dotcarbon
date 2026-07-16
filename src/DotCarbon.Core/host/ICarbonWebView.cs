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

    /// <summary>Constrain (or release, with 0) the smallest the window may be resized to. Task 3.2.</summary>
    void SetMinSize(int width, int height);

    /// <summary>Constrain (or release, with 0) the largest the window may be resized to. Task 3.2.</summary>
    void SetMaxSize(int width, int height);

    /// <summary>The content (webview) area size, excluding decorations. Task 3.2.</summary>
    (int Width, int Height) GetInnerSize();

    /// <summary>The full window size, including title bar and borders. Task 3.2.</summary>
    (int Width, int Height) GetOuterSize();

    /// <summary>The content area's top-left in screen coordinates. Task 3.2.</summary>
    (int X, int Y) GetInnerPosition();

    /// <summary>The window's top-left in screen coordinates (frame origin). Task 3.2.</summary>
    (int X, int Y) GetOuterPosition();
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

    /// <summary>
    /// Begin an OS window-move drag from the current pointer press. Called from a mousedown on a
    /// drag region so a frameless or full-window app can still be moved. Task 3.8.
    /// </summary>
    void StartDragging();

    // --- chrome & behavior (Task 3.3) --------------------------------------------------------

    /// <summary>Show or hide the OS window frame (title bar + border) at runtime.</summary>
    void SetDecorations(bool decorations);

    /// <summary>Enable or disable the window's close control.</summary>
    void SetClosable(bool closable);

    /// <summary>Enable or disable the window's minimize control.</summary>
    void SetMinimizable(bool minimizable);

    /// <summary>Enable or disable the window's maximize/zoom control.</summary>
    void SetMaximizable(bool maximizable);

    /// <summary>Keep the window below all others (the opposite of always-on-top).</summary>
    void SetAlwaysOnBottom(bool alwaysOnBottom);

    /// <summary>Hide the window from the taskbar / window list. No per-window effect on macOS.</summary>
    void SetSkipTaskbar(bool skip);

    /// <summary>Exclude the window from screen capture / screenshots where the OS supports it.</summary>
    void SetContentProtected(bool protectedContent);

    /// <summary>Make the window click-through, passing pointer events to whatever is behind it.</summary>
    void SetIgnoreCursorEvents(bool ignore);

    /// <summary>Replace the window/taskbar icon at runtime.</summary>
    void SetIcon(string path);

    // --- cursor (Task 3.4) -------------------------------------------------------------------

    /// <summary>Set the cursor shape by name ("default", "pointer", "text", "crosshair", ...).</summary>
    void SetCursorIcon(string icon);

    /// <summary>Show or hide the mouse cursor.</summary>
    void SetCursorVisible(bool visible);

    /// <summary>Confine the cursor to the window (grab) or release it.</summary>
    void SetCursorGrab(bool grab);

    /// <summary>Move the cursor to a point relative to the window's top-left content.</summary>
    void SetCursorPosition(int x, int y);

    /// <summary>Navigate the webview to a URI (carbon://, http://localhost dev server, or an external URL).</summary>
    void LoadUri(Uri uri);

    /// <summary>Load a raw HTML string (used for the no-frontend fallback screen).</summary>
    void LoadString(string html);

    /// <summary>Push a bridge message (command response or event) to the JavaScript side.</summary>
    Task SendMessageAsync(string message);

    void Close();
}
