namespace DotCarbon.Core.Runtime;

public enum CarbonLifecycleEventKind
{
    Starting,
    Ready,
    ExitRequested,
    Exiting,
    Exited,
    WindowCreating,
    WindowCreated,
    WindowCloseRequested,
    WindowClosed,
    WindowFocused,
    WindowBlurred,
    WindowMoved,
    WindowResized,
    WindowMinimized,
    WindowMaximized,
    WindowRestored,
}

public sealed class CarbonLifecycleEvent : EventArgs
{
    internal CarbonLifecycleEvent(
        CarbonLifecycleEventKind kind,
        AppHandle app,
        CarbonWindow? window = null,
        object? data = null)
    {
        Kind = kind;
        App = app;
        Window = window;
        Data = data;
    }

    public CarbonLifecycleEventKind Kind { get; }
    public AppHandle App { get; }
    public CarbonWindow? Window { get; }
    public object? Data { get; }
    public bool Cancel { get; set; }
}

public readonly record struct CarbonWindowPosition(int X, int Y);
public readonly record struct CarbonWindowSize(int Width, int Height);
