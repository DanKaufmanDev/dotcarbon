using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Window;

[CarbonPlugin("Window", description: "Create and control labeled windows and webviews.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("window:default", "Allow all window commands.", Commands = new[] { "window:*" })]
public partial class WindowPlugin : IPlugin
{
    private readonly AppHandle _app;

    public WindowPlugin(AppHandle app)
    {
        _app = app;
    }

    public string Namespace => "window";

    [CarbonCommand("create")]
    public Task<WindowState> Create(CreateWindowArgs args)
    {
        var window = _app.CreateWindow(new CarbonWindowOptions
        {
            Label = args.Label,
            Url = args.Url,
            ParentLabel = args.ParentLabel,
            Capabilities = args.Capabilities is null ? [] : [.. args.Capabilities],
            Title = args.Title ?? _app.Config.App.Name,
            Width = args.Width ?? 800,
            Height = args.Height ?? 600,
            MinWidth = args.MinWidth,
            MinHeight = args.MinHeight,
            MaxWidth = args.MaxWidth,
            MaxHeight = args.MaxHeight,
            X = args.X,
            Y = args.Y,
            Center = args.Center ?? true,
            Resizable = args.Resizable ?? true,
            Fullscreen = args.Fullscreen ?? false,
            Maximized = args.Maximized ?? false,
            AlwaysOnTop = args.AlwaysOnTop ?? false,
            Decorations = args.Decorations ?? true,
            Transparent = args.Transparent ?? false,
            DevTools = args.DevTools ?? true,
            ContextMenu = args.ContextMenu ?? true,
            Icon = args.Icon,
        });
        return Task.FromResult(ToState(window.Label, window.Native));
    }

    [CarbonCommand("get_all")]
    public Task<List<WindowState>> GetAll()
    {
        var states = _app.Windows
            .Select(window => ToState(window.Label, window.Native))
            .ToList();
        return Task.FromResult(states);
    }

    [CarbonCommand("get_by_label")]
    public Task<WindowState?> GetByLabel(TargetWindowArgs args)
    {
        return Task.FromResult(!string.IsNullOrWhiteSpace(args.Label) &&
            _app.TryGetWindow(args.Label, out var window)
            ? ToState(window.Label, window.Native)
            : null);
    }

    [CarbonCommand("set_title")]
    public Task SetTitle(SetTitleArgs args)
    {
        Resolve(args.Label).View.SetTitle(args.Title);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_size")]
    public Task SetSize(SetSizeArgs args)
    {
        Resolve(args.Label).View.SetSize(args.Width, args.Height);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_position")]
    public Task SetPosition(SetPositionArgs args)
    {
        Resolve(args.Label).View.SetPosition(args.X, args.Y);
        return Task.CompletedTask;
    }

    [CarbonCommand("center")]
    public Task Center(TargetWindowArgs args)
    {
        Resolve(args.Label).View.Center();
        return Task.CompletedTask;
    }

    public Task Center() => Center(new TargetWindowArgs());

    [CarbonCommand("minimize")]
    public Task Minimize(TargetWindowArgs args)
    {
        Resolve(args.Label).View.SetMinimized(true);
        return Task.CompletedTask;
    }

    public Task Minimize() => Minimize(new TargetWindowArgs());

    [CarbonCommand("maximize")]
    public Task Maximize(TargetWindowArgs args)
    {
        Resolve(args.Label).View.SetMaximized(true);
        return Task.CompletedTask;
    }

    public Task Maximize() => Maximize(new TargetWindowArgs());

    [CarbonCommand("unmaximize")]
    public Task Unmaximize(TargetWindowArgs args)
    {
        Resolve(args.Label).View.SetMaximized(false);
        return Task.CompletedTask;
    }

    public Task Unmaximize() => Unmaximize(new TargetWindowArgs());

    [CarbonCommand("set_fullscreen")]
    public Task SetFullscreen(SetFullscreenArgs args)
    {
        Resolve(args.Label).View.SetFullscreen(args.Fullscreen);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_always_on_top")]
    public Task SetAlwaysOnTop(SetAlwaysOnTopArgs args)
    {
        Resolve(args.Label).View.SetAlwaysOnTop(args.AlwaysOnTop);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_resizable")]
    public Task SetResizable(SetResizableArgs args)
    {
        Resolve(args.Label).View.SetResizable(args.Resizable);
        return Task.CompletedTask;
    }

    [CarbonCommand("close")]
    public Task Close(TargetWindowArgs args)
    {
        Resolve(args.Label).View.Close();
        return Task.CompletedTask;
    }

    public Task Close() => Close(new TargetWindowArgs());

    [CarbonCommand("get_state")]
    public Task<WindowState> GetState(TargetWindowArgs args)
    {
        var resolved = Resolve(args.Label);
        return Task.FromResult(ToState(resolved.Label, resolved.View));
    }

    public Task<WindowState> GetState() => GetState(new TargetWindowArgs());

    // --- visibility & focus (Task 3.1) -------------------------------------------------------

    [CarbonCommand("show")]
    public Task Show(TargetWindowArgs args)
    {
        Resolve(args.Label).View.Show();
        return Task.CompletedTask;
    }

    public Task Show() => Show(new TargetWindowArgs());

    [CarbonCommand("hide")]
    public Task Hide(TargetWindowArgs args)
    {
        Resolve(args.Label).View.Hide();
        return Task.CompletedTask;
    }

    public Task Hide() => Hide(new TargetWindowArgs());

    [CarbonCommand("set_focus")]
    public Task SetFocus(TargetWindowArgs args)
    {
        Resolve(args.Label).View.SetFocus();
        return Task.CompletedTask;
    }

    public Task SetFocus() => SetFocus(new TargetWindowArgs());

    [CarbonCommand("is_visible")]
    public Task<bool> IsVisible(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.IsVisible);

    public Task<bool> IsVisible() => IsVisible(new TargetWindowArgs());

    [CarbonCommand("is_focused")]
    public Task<bool> IsFocused(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.IsFocused);

    public Task<bool> IsFocused() => IsFocused(new TargetWindowArgs());

    [CarbonCommand("request_user_attention")]
    public Task RequestUserAttention(TargetWindowArgs args)
    {
        Resolve(args.Label).View.RequestUserAttention();
        return Task.CompletedTask;
    }

    public Task RequestUserAttention() => RequestUserAttention(new TargetWindowArgs());

    /// <summary>
    /// Begin dragging the window (Task 3.8). Meant to be called from a mousedown on a custom title
    /// bar / drag region, so a frameless or full-window app can still be moved.
    /// </summary>
    [CarbonCommand("start_dragging")]
    public Task StartDragging(TargetWindowArgs args)
    {
        Resolve(args.Label).View.StartDragging();
        return Task.CompletedTask;
    }

    public Task StartDragging() => StartDragging(new TargetWindowArgs());

    // --- chrome & behavior (Task 3.3) --------------------------------------------------------

    [CarbonCommand("set_decorations")]
    public Task SetDecorations(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetDecorations(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_closable")]
    public Task SetClosable(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetClosable(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_minimizable")]
    public Task SetMinimizable(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetMinimizable(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_maximizable")]
    public Task SetMaximizable(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetMaximizable(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_always_on_bottom")]
    public Task SetAlwaysOnBottom(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetAlwaysOnBottom(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_skip_taskbar")]
    public Task SetSkipTaskbar(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetSkipTaskbar(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_content_protected")]
    public Task SetContentProtected(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetContentProtected(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_ignore_cursor_events")]
    public Task SetIgnoreCursorEvents(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetIgnoreCursorEvents(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_icon")]
    public Task SetIcon(SetIconArgs args)
    {
        Resolve(args.Label).View.SetIcon(args.Path);
        return Task.CompletedTask;
    }

    // --- geometry depth (Task 3.2) -----------------------------------------------------------

    [CarbonCommand("set_min_size")]
    public Task SetMinSize(SetMinSizeArgs args)
    {
        Resolve(args.Label).View.SetMinSize(args.Width, args.Height);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_max_size")]
    public Task SetMaxSize(SetMaxSizeArgs args)
    {
        Resolve(args.Label).View.SetMaxSize(args.Width, args.Height);
        return Task.CompletedTask;
    }

    [CarbonCommand("inner_size")]
    public Task<WindowSize> InnerSize(TargetWindowArgs args)
    {
        var (w, h) = Resolve(args.Label).View.GetInnerSize();
        return Task.FromResult(new WindowSize(w, h));
    }

    public Task<WindowSize> InnerSize() => InnerSize(new TargetWindowArgs());

    [CarbonCommand("outer_size")]
    public Task<WindowSize> OuterSize(TargetWindowArgs args)
    {
        var (w, h) = Resolve(args.Label).View.GetOuterSize();
        return Task.FromResult(new WindowSize(w, h));
    }

    public Task<WindowSize> OuterSize() => OuterSize(new TargetWindowArgs());

    [CarbonCommand("inner_position")]
    public Task<WindowPosition> InnerPosition(TargetWindowArgs args)
    {
        var (x, y) = Resolve(args.Label).View.GetInnerPosition();
        return Task.FromResult(new WindowPosition(x, y));
    }

    public Task<WindowPosition> InnerPosition() => InnerPosition(new TargetWindowArgs());

    [CarbonCommand("outer_position")]
    public Task<WindowPosition> OuterPosition(TargetWindowArgs args)
    {
        var (x, y) = Resolve(args.Label).View.GetOuterPosition();
        return Task.FromResult(new WindowPosition(x, y));
    }

    public Task<WindowPosition> OuterPosition() => OuterPosition(new TargetWindowArgs());

    [CarbonCommand("is_maximized")]
    public Task<bool> IsMaximized(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.IsMaximized);

    public Task<bool> IsMaximized() => IsMaximized(new TargetWindowArgs());

    [CarbonCommand("is_minimized")]
    public Task<bool> IsMinimized(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.IsMinimized);

    public Task<bool> IsMinimized() => IsMinimized(new TargetWindowArgs());

    [CarbonCommand("is_fullscreen")]
    public Task<bool> IsFullscreen(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.IsFullscreen);

    public Task<bool> IsFullscreen() => IsFullscreen(new TargetWindowArgs());

    private (string Label, ICarbonWebView View) Resolve(string? label)
    {
        var window = string.IsNullOrWhiteSpace(label)
            ? _app.CurrentWindow
            : _app.GetWindow(label);
        return (window.Label, window.Native);
    }

    private static WindowState ToState(string label, ICarbonWebView window) => new(
        Label: label,
        Title: window.Title,
        Width: window.Width,
        Height: window.Height,
        X: window.X,
        Y: window.Y,
        Fullscreen: window.IsFullscreen,
        Maximized: window.IsMaximized,
        Minimized: window.IsMinimized,
        AlwaysOnTop: window.IsAlwaysOnTop,
        Resizable: window.IsResizable,
        Visible: window.IsVisible,
        Focused: window.IsFocused);
}
