namespace DotCarbon.Plugins.Window;

public record SetTitleArgs(string Title);
public record SetSizeArgs(int Width, int Height);
public record SetPositionArgs(int X, int Y);
public record SetAlwaysOnTopArgs(bool AlwaysOnTop);
public record SetFullscreenArgs(bool Fullscreen);
public record SetResizableArgs(bool Resizable);

public record WindowState(
    string Title,
    int Width,
    int Height,
    int X,
    int Y,
    bool Fullscreen,
    bool Maximized,
    bool Minimized,
    bool AlwaysOnTop,
    bool Resizable
);