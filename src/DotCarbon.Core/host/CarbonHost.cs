using Photino.NET;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Core.Host;

/// <summary>
/// Compatibility facade for the original single-window API. New applications should use
/// <see cref="CarbonApp"/> directly.
/// </summary>
public sealed class CarbonHost
{
    private readonly CarbonApp _app;

    public CarbonHost(CarbonConfig config)
    {
        _app = CarbonApp.Create(config);
    }

    public AppHandle App => _app.Handle;

    public PhotinoWindow Window => App.GetWindow(App.Config.Window.Label).NativeWindow;

    public CarbonHost WithPlugin(IPlugin plugin)
    {
        _app.WithPlugin(plugin);
        return this;
    }

    public CarbonHost WithPlugin(Func<PhotinoWindow, IPlugin> factory)
    {
        _app.WithWindowPlugin((_, window) => factory(window.NativeWindow));
        return this;
    }

    public CarbonHost Setup(Action<AppHandle> setup)
    {
        _app.Setup(setup);
        return this;
    }

    public CarbonHost OnLifecycle(Action<CarbonLifecycleEvent> handler)
    {
        _app.OnLifecycle(handler);
        return this;
    }

    public void Run() => _app.Run();
}
