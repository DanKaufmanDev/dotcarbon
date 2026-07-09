using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using Photino.NET;

namespace DotCarbon.Plugins.Window;

public partial class WindowPlugin : IPlugin
{
    public string Namespace => "window";

    private readonly PhotinoWindow _window;

    public WindowPlugin(PhotinoWindow window)
    {
        _window = window;
    }

    [CarbonCommand("set_title")]
    public Task SetTitle(SetTitleArgs args)
    {
        _window.SetTitle(args.Title);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_size")]
    public Task SetSize(SetSizeArgs args)
    {
        _window.SetSize(args.Width, args.Height);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_position")]
    public Task SetPosition(SetPositionArgs args)
    {
        _window.SetLocation(new System.Drawing.Point(args.X, args.Y));
        return Task.CompletedTask;
    }

    [CarbonCommand("center")]
    public Task Center()
    {
        _window.SetUseOsDefaultLocation(true);
        return Task.CompletedTask;
    }

    [CarbonCommand("minimize")]
    public Task Minimize()
    {
        _window.SetMinimized(true);
        return Task.CompletedTask;
    }

    [CarbonCommand("maximize")]
    public Task Maximize()
    {
        _window.SetMaximized(true);
        return Task.CompletedTask;
    }

    [CarbonCommand("unmaximize")]
    public Task Unmaximize()
    {
        _window.SetMaximized(false);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_fullscreen")]
    public Task SetFullscreen(SetFullscreenArgs args)
    {
        _window.SetFullScreen(args.Fullscreen);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_always_on_top")]
    public Task SetAlwaysOnTop(SetAlwaysOnTopArgs args)
    {
        _window.SetTopMost(args.AlwaysOnTop);
        return Task.CompletedTask;
    }

    [CarbonCommand("set_resizable")]
    public Task SetResizable(SetResizableArgs args)
    {
        _window.SetResizable(args.Resizable);
        return Task.CompletedTask;
    }

    [CarbonCommand("close")]
    public Task Close()
    {
        _window.Close();
        return Task.CompletedTask;
    }

    [CarbonCommand("get_state")]
    public Task<WindowState> GetState()
    {
        var size = _window.Size;
        var location = _window.Location;

        return Task.FromResult(new WindowState(
            Title: _window.Title,
            Width: size.Width,
            Height: size.Height,
            X: location.X,
            Y: location.Y,
            Fullscreen: _window.FullScreen,
            Maximized: _window.Maximized,
            Minimized: _window.Minimized,
            AlwaysOnTop: _window.Topmost,
            Resizable: _window.Resizable
        ));
    }
}