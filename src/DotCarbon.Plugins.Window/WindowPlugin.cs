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

    // Task 3.7: which windows the frontend is intercepting close for, and which have had a close
    // forced through (window:destroy). Both keyed by window label.
    private readonly HashSet<string> _interceptClose = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forceClose = new(StringComparer.Ordinal);

    public WindowPlugin(AppHandle app)
    {
        _app = app;
    }

    public string Namespace => "window";

    // --- lifecycle events → frontend (Task 3.7) ----------------------------------------------

    /// <summary>
    /// Forward native window lifecycle events to the frontend as <c>window:*</c> events, and drive the
    /// close-requested veto. This runs synchronously inside the native close callback, so the veto
    /// decision (<see cref="CarbonLifecycleEvent.Cancel"/>) is set here-and-now; the notification to
    /// JS is fire-and-forget.
    /// </summary>
    public ValueTask OnLifecycleAsync(CarbonLifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent.Window is not { } window) return ValueTask.CompletedTask;
        var label = window.Label;

        switch (lifecycleEvent.Kind)
        {
            case CarbonLifecycleEventKind.WindowCloseRequested when ShouldVetoClose(label):
                // Keep the window open and let the frontend decide; it closes for real via
                // window:destroy once it has finished (or never, to cancel the close).
                lifecycleEvent.Cancel = true;
                Emit("window:close-requested", new WindowEventPayload(label),
                    WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowFocused:
                Emit("window:focus", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowBlurred:
                Emit("window:blur", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowMoved when lifecycleEvent.Data is CarbonWindowPosition p:
                Emit("window:moved", new WindowMovedPayload(label, p.X, p.Y),
                    WindowJsonContext.Default.WindowMovedPayload);
                break;
            case CarbonLifecycleEventKind.WindowResized when lifecycleEvent.Data is CarbonWindowSize s:
                Emit("window:resized", new WindowResizedPayload(label, s.Width, s.Height),
                    WindowJsonContext.Default.WindowResizedPayload);
                break;
            case CarbonLifecycleEventKind.WindowMinimized:
                Emit("window:minimized", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowMaximized:
                Emit("window:maximized", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowRestored:
                Emit("window:restored", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
            case CarbonLifecycleEventKind.WindowClosed:
                Emit("window:closed", new WindowEventPayload(label), WindowJsonContext.Default.WindowEventPayload);
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Whether a close should be vetoed. A forced close (window:destroy) is honoured once and clears
    /// the flag; otherwise the window is vetoed while the frontend is intercepting it.
    /// </summary>
    public bool ShouldVetoClose(string label)
    {
        if (_forceClose.Remove(label)) return false;
        return _interceptClose.Contains(label);
    }

    private void Emit<T>(string name, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        var task = _app.EmitAsync(new CarbonEventName<T>(name), payload, info);
        if (!task.IsCompletedSuccessfully) _ = ObserveEmit(task, name);
    }

    private static async Task ObserveEmit(Task task, string name)
    {
        try { await task; }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Window event '{name}' failed: {ex.Message}"); }
    }

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

    // --- close interception (Task 3.7) -------------------------------------------------------

    /// <summary>
    /// Turn close interception on/off for a window. While on, the OS close is held and a
    /// <c>window:close-requested</c> event is emitted instead; the frontend closes for real with
    /// <c>window:destroy</c>. Set automatically by the JS <c>onCloseRequested</c> helper.
    /// </summary>
    [CarbonCommand("set_close_interception")]
    public Task SetCloseInterception(SetFlagArgs args)
    {
        var label = Resolve(args.Label).Label;
        if (args.Value) _interceptClose.Add(label);
        else _interceptClose.Remove(label);
        return Task.CompletedTask;
    }

    /// <summary>Close a window for real, bypassing any interception.</summary>
    [CarbonCommand("destroy")]
    public Task Destroy(TargetWindowArgs args)
    {
        var (label, view) = Resolve(args.Label);
        _forceClose.Add(label);
        view.Close();
        return Task.CompletedTask;
    }

    public Task Destroy() => Destroy(new TargetWindowArgs());

    // --- theme (Task 3.6) --------------------------------------------------------------------

    [CarbonCommand("get_theme")]
    public Task<string> GetTheme(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.GetTheme());

    public Task<string> GetTheme() => GetTheme(new TargetWindowArgs());

    [CarbonCommand("set_theme")]
    public Task SetTheme(SetThemeArgs args)
    {
        Resolve(args.Label).View.SetTheme(args.Theme);
        return Task.CompletedTask;
    }

    // --- monitors (Task 3.5) -----------------------------------------------------------------

    [CarbonCommand("available_monitors")]
    public Task<List<MonitorInfo>> AvailableMonitors(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.GetMonitors().Select(ToMonitor).ToList());

    public Task<List<MonitorInfo>> AvailableMonitors() => AvailableMonitors(new TargetWindowArgs());

    [CarbonCommand("primary_monitor")]
    public Task<MonitorInfo?> PrimaryMonitor(TargetWindowArgs args) =>
        Task.FromResult(Map(Resolve(args.Label).View.GetPrimaryMonitor()));

    public Task<MonitorInfo?> PrimaryMonitor() => PrimaryMonitor(new TargetWindowArgs());

    [CarbonCommand("current_monitor")]
    public Task<MonitorInfo?> CurrentMonitor(TargetWindowArgs args) =>
        Task.FromResult(Map(Resolve(args.Label).View.GetCurrentMonitor()));

    public Task<MonitorInfo?> CurrentMonitor() => CurrentMonitor(new TargetWindowArgs());

    [CarbonCommand("scale_factor")]
    public Task<double> ScaleFactor(TargetWindowArgs args) =>
        Task.FromResult(Resolve(args.Label).View.GetScaleFactor());

    public Task<double> ScaleFactor() => ScaleFactor(new TargetWindowArgs());

    private static MonitorInfo? Map(DotCarbon.Core.Host.CarbonMonitorInfo? m) => m is null ? null : ToMonitor(m);

    private static MonitorInfo ToMonitor(DotCarbon.Core.Host.CarbonMonitorInfo m) => new(
        m.Name,
        new WindowPosition(m.X, m.Y),
        new WindowSize(m.Width, m.Height),
        new WindowPosition(m.WorkX, m.WorkY),
        new WindowSize(m.WorkWidth, m.WorkHeight),
        m.ScaleFactor);

    // --- cursor (Task 3.4) -------------------------------------------------------------------

    [CarbonCommand("set_cursor_icon")]
    public Task SetCursorIcon(SetCursorIconArgs args)
    {
        Resolve(args.Label).View.SetCursorIcon(args.Icon);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_cursor_visible")]
    public Task SetCursorVisible(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetCursorVisible(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_cursor_grab")]
    public Task SetCursorGrab(SetFlagArgs args)
    {
        Resolve(args.Label).View.SetCursorGrab(args.Value);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_cursor_position")]
    public Task SetCursorPosition(SetPositionArgs args)
    {
        Resolve(args.Label).View.SetCursorPosition(args.X, args.Y);
        return Task.CompletedTask;
    }

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
