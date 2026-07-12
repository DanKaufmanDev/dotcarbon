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
        Resizable: window.IsResizable);
}
