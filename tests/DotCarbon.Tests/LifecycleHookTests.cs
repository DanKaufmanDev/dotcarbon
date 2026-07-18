using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.5: the ergonomic app lifecycle hooks — ready, exit-requested (preventable), before-exit,
/// and window-all-closed.
/// </summary>
public class LifecycleHookTests
{
    private static CarbonConfig Config() => new() { Window = new WindowConfig { Label = "main" } };

    [Fact]
    public void OnReady_fires_on_start()
    {
        var ready = false;
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost()).OnReady(_ => ready = true);
        try { app.Start(); Assert.True(ready); }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void OnExitRequested_can_prevent_the_close()
    {
        var asked = false;
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost())
            .OnExitRequested(request => { asked = true; request.Prevent(); });
        var handle = app.Start();
        try
        {
            // Closing the main window asks to exit; the hook prevents it, so the close is vetoed.
            var vetoed = app.HandleWindowClosing(handle.GetWindow("main"));
            Assert.True(asked);
            Assert.True(vetoed);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void OnExitRequested_allows_the_close_when_not_prevented()
    {
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost())
            .OnExitRequested(_ => { /* observe only */ });
        var handle = app.Start();
        try { Assert.False(app.HandleWindowClosing(handle.GetWindow("main"))); }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void OnWindowAllClosed_and_OnBeforeExit_fire_when_the_last_window_closes()
    {
        var allClosed = 0;
        var beforeExit = 0;
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost())
            .OnWindowAllClosed(() => allClosed++)
            .OnBeforeExit(() => beforeExit++);
        var handle = app.Start();
        try
        {
            app.HandleWindowClosing(handle.GetWindow("main"));
            Assert.Equal(1, allClosed);
            Assert.Equal(1, beforeExit);
        }
        finally { app.Shutdown(); }
    }
}
