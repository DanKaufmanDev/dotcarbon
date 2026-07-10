using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Photino.NET;

namespace DotCarbon.Plugins.Window;

public partial class WindowPlugin : IPlugin
{
    private readonly AppHandle? _app;
    private readonly PhotinoWindow? _legacyWindow;

    public WindowPlugin(AppHandle app)
    {
        _app = app;
    }

    public WindowPlugin(PhotinoWindow window)
    {
        _legacyWindow = window;
    }

    public string Namespace => "window";

    [CarbonCommand("create")]
    public Task<WindowState> Create(CreateWindowArgs args)
    {
        var app = RequireApp();
        var window = app.CreateWindow(new CarbonWindowOptions
        {
            Label = args.Label,
            Url = args.Url,
            ParentLabel = args.ParentLabel,
            Title = args.Title ?? app.Config.App.Name,
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
        return Task.FromResult(ToState(window.Label, window.NativeWindow));
    }

    [CarbonCommand("get_all")]
    public Task<List<WindowState>> GetAll()
    {
        var states = RequireApp().Windows
            .Select(window => ToState(window.Label, window.NativeWindow))
            .ToList();
        return Task.FromResult(states);
    }

    [CarbonCommand("get_by_label")]
    public Task<WindowState?> GetByLabel(TargetWindowArgs args)
    {
        var app = RequireApp();
        return Task.FromResult(!string.IsNullOrWhiteSpace(args.Label) &&
            app.TryGetWindow(args.Label, out var window)
            ? ToState(window.Label, window.NativeWindow)
            : null);
    }

    [CarbonCommand("set_title")]
    public Task SetTitle(SetTitleArgs args)
    {
        Resolve(args.Label).Window.SetTitle(args.Title);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_size")]
    public Task SetSize(SetSizeArgs args)
    {
        Resolve(args.Label).Window.SetSize(args.Width, args.Height);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_position")]
    public Task SetPosition(SetPositionArgs args)
    {
        Resolve(args.Label).Window.SetLocation(new System.Drawing.Point(args.X, args.Y));
        return Task.CompletedTask;
    }

    [CarbonCommand("center")]
    public Task Center(TargetWindowArgs args)
    {
        Resolve(args.Label).Window.SetUseOsDefaultLocation(true);
        return Task.CompletedTask;
    }

    public Task Center() => Center(new TargetWindowArgs());

    [CarbonCommand("minimize")]
    public Task Minimize(TargetWindowArgs args)
    {
        Resolve(args.Label).Window.SetMinimized(true);
        return Task.CompletedTask;
    }

    public Task Minimize() => Minimize(new TargetWindowArgs());

    [CarbonCommand("maximize")]
    public Task Maximize(TargetWindowArgs args)
    {
        Resolve(args.Label).Window.SetMaximized(true);
        return Task.CompletedTask;
    }

    public Task Maximize() => Maximize(new TargetWindowArgs());

    [CarbonCommand("unmaximize")]
    public Task Unmaximize(TargetWindowArgs args)
    {
        Resolve(args.Label).Window.SetMaximized(false);
        return Task.CompletedTask;
    }

    public Task Unmaximize() => Unmaximize(new TargetWindowArgs());

    [CarbonCommand("set_fullscreen")]
    public Task SetFullscreen(SetFullscreenArgs args)
    {
        Resolve(args.Label).Window.SetFullScreen(args.Fullscreen);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_always_on_top")]
    public Task SetAlwaysOnTop(SetAlwaysOnTopArgs args)
    {
        Resolve(args.Label).Window.SetTopMost(args.AlwaysOnTop);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_resizable")]
    public Task SetResizable(SetResizableArgs args)
    {
        Resolve(args.Label).Window.SetResizable(args.Resizable);
        return Task.CompletedTask;
    }

    [CarbonCommand("close")]
    public Task Close(TargetWindowArgs args)
    {
        Resolve(args.Label).Window.Close();
        return Task.CompletedTask;
    }

    public Task Close() => Close(new TargetWindowArgs());

    [CarbonCommand("get_state")]
    public Task<WindowState> GetState(TargetWindowArgs args)
    {
        var resolved = Resolve(args.Label);
        return Task.FromResult(ToState(resolved.Label, resolved.Window));
    }

    public Task<WindowState> GetState() => GetState(new TargetWindowArgs());

    private (string Label, PhotinoWindow Window) Resolve(string? label)
    {
        if (_app is not null)
        {
            var window = string.IsNullOrWhiteSpace(label)
                ? _app.CurrentWindow
                : _app.GetWindow(label);
            return (window.Label, window.NativeWindow);
        }

        return ("main", _legacyWindow
            ?? throw new InvalidOperationException("WindowPlugin has no application or window."));
    }

    private AppHandle RequireApp() => _app
        ?? throw new InvalidOperationException(
            "This command requires WindowPlugin(AppHandle). CarbonHost's PhotinoWindow constructor only supports the current window.");

    private static WindowState ToState(string label, PhotinoWindow window)
    {
        var size = window.Size;
        var location = window.Location;
        return new WindowState(
            Label: label,
            Title: window.Title,
            Width: size.Width,
            Height: size.Height,
            X: location.X,
            Y: location.Y,
            Fullscreen: window.FullScreen,
            Maximized: window.Maximized,
            Minimized: window.Minimized,
            AlwaysOnTop: window.Topmost,
            Resizable: window.Resizable);
    }
}
