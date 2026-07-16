namespace DotCarbon.Plugins.Window;

public record TargetWindowArgs(string? Label = null);
public record SetTitleArgs(string Title, string? Label = null);
public record SetSizeArgs(int Width, int Height, string? Label = null);
public record SetPositionArgs(int X, int Y, string? Label = null);
public record SetFlagArgs(bool Value, string? Label = null);
public record SetIconArgs(string Path, string? Label = null);
public record SetCursorIconArgs(string Icon, string? Label = null);
public record SetMinSizeArgs(int Width, int Height, string? Label = null);
public record SetMaxSizeArgs(int Width, int Height, string? Label = null);

/// <summary>A window dimension in physical pixels (Task 3.2).</summary>
public record WindowSize(int Width, int Height);

/// <summary>A window coordinate in physical screen pixels (Task 3.2).</summary>
public record WindowPosition(int X, int Y);
public record SetAlwaysOnTopArgs(bool AlwaysOnTop, string? Label = null);
public record SetFullscreenArgs(bool Fullscreen, string? Label = null);
public record SetResizableArgs(bool Resizable, string? Label = null);

public record CreateWindowArgs(
    string Label,
    string? Url = null,
    string? ParentLabel = null,
    string? Title = null,
    int? Width = null,
    int? Height = null,
    int? MinWidth = null,
    int? MinHeight = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    int? X = null,
    int? Y = null,
    bool? Center = null,
    bool? Resizable = null,
    bool? Fullscreen = null,
    bool? Maximized = null,
    bool? AlwaysOnTop = null,
    bool? Decorations = null,
    bool? Transparent = null,
    bool? DevTools = null,
    bool? ContextMenu = null,
    string? Icon = null,
    List<string>? Capabilities = null);

public record WindowState(
    string Label,
    string Title,
    int Width,
    int Height,
    int X,
    int Y,
    bool Fullscreen,
    bool Maximized,
    bool Minimized,
    bool AlwaysOnTop,
    bool Resizable,
    bool Visible = true,
    bool Focused = false)
{
    public WindowState(
        string title,
        int width,
        int height,
        int x,
        int y,
        bool fullscreen,
        bool maximized,
        bool minimized,
        bool alwaysOnTop,
        bool resizable)
        : this(
            "main", title, width, height, x, y, fullscreen,
            maximized, minimized, alwaysOnTop, resizable)
    {
    }
}
