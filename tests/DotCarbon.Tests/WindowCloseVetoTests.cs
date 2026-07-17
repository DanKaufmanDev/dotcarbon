using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Window;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 3.7: the close-requested veto state machine. Interception holds the close until the frontend
/// forces it through with window:destroy — a one-shot that clears itself.
/// </summary>
public class WindowCloseVetoTests
{
    private static (AppHandle Handle, WindowPlugin Plugin, System.Action Shutdown) Build()
    {
        var config = new CarbonConfig { Window = new WindowConfig { Label = "main" } };
        var app = CarbonApp.Create(config).UsePlatform(new VetoHost());
        var handle = app.Start();
        return (handle, new WindowPlugin(handle), app.Shutdown);
    }

    [Fact]
    public void No_interception_allows_close()
    {
        var (_, plugin, shutdown) = Build();
        try { Assert.False(plugin.ShouldVetoClose("main")); }
        finally { shutdown(); }
    }

    [Fact]
    public void Interception_vetoes_close()
    {
        var (_, plugin, shutdown) = Build();
        try
        {
            plugin.SetCloseInterception(new SetFlagArgs(true, "main"));
            Assert.True(plugin.ShouldVetoClose("main"));
        }
        finally { shutdown(); }
    }

    [Fact]
    public void Destroy_forces_one_close_then_interception_resumes()
    {
        var (_, plugin, shutdown) = Build();
        try
        {
            plugin.SetCloseInterception(new SetFlagArgs(true, "main"));
            plugin.Destroy(new TargetWindowArgs("main"));

            // The forced close is honoured exactly once...
            Assert.False(plugin.ShouldVetoClose("main"));
            // ...then interception is back in force.
            Assert.True(plugin.ShouldVetoClose("main"));
        }
        finally { shutdown(); }
    }

    [Fact]
    public void Turning_interception_off_allows_close()
    {
        var (_, plugin, shutdown) = Build();
        try
        {
            plugin.SetCloseInterception(new SetFlagArgs(true, "main"));
            plugin.SetCloseInterception(new SetFlagArgs(false, "main"));
            Assert.False(plugin.ShouldVetoClose("main"));
        }
        finally { shutdown(); }
    }

    private sealed class VetoHost : ICarbonPlatformHost
    {
        public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new VetoWebView();
        public void Run(ICarbonWebView mainWebView) { }
    }

    // A no-op webview; the veto logic never touches the native side except Close(), which is safe here.
    private sealed class VetoWebView : ICarbonWebView
    {
        public string Title => "main";
        public int Width => 800; public int Height => 600; public int X => 0; public int Y => 0;
        public bool IsFullscreen => false; public bool IsMaximized => false; public bool IsMinimized => false;
        public bool IsAlwaysOnTop => false; public bool IsResizable => true; public bool IsVisible => true;
        public bool IsFocused => false;
        public void SetTitle(string title) { } public void SetSize(int w, int h) { } public void SetPosition(int x, int y) { }
        public void Center() { } public void SetMinSize(int w, int h) { } public void SetMaxSize(int w, int h) { }
        public (int, int) GetInnerSize() => (Width, Height); public (int, int) GetOuterSize() => (Width, Height);
        public (int, int) GetInnerPosition() => (0, 0); public (int, int) GetOuterPosition() => (0, 0);
        public void SetMinimized(bool m) { } public void SetMaximized(bool m) { } public void SetFullscreen(bool f) { }
        public void SetAlwaysOnTop(bool a) { } public void SetResizable(bool r) { }
        public void Show() { } public void Hide() { } public void SetFocus() { } public void RequestUserAttention() { }
        public void StartDragging() { }
        public void SetDecorations(bool d) { } public void SetClosable(bool c) { } public void SetMinimizable(bool m) { }
        public void SetMaximizable(bool m) { } public void SetAlwaysOnBottom(bool a) { } public void SetSkipTaskbar(bool s) { }
        public void SetContentProtected(bool p) { } public void SetIgnoreCursorEvents(bool i) { } public void SetIcon(string p) { }
        public void SetCursorIcon(string i) { } public void SetCursorVisible(bool v) { } public void SetCursorGrab(bool g) { }
        public void SetCursorPosition(int x, int y) { }
        public IReadOnlyList<CarbonMonitorInfo> GetMonitors() => [GetPrimaryMonitor()!];
        public CarbonMonitorInfo? GetPrimaryMonitor() => new(null, 0, 0, 800, 600, 0, 0, 800, 600, 1.0);
        public CarbonMonitorInfo? GetCurrentMonitor() => GetPrimaryMonitor();
        public double GetScaleFactor() => 1.0;
        public string GetTheme() => "light"; public void SetTheme(string t) { }
        public void SetProgressBar(string status, int progress) { } public void SetBadge(string? label) { }
        public void SetEffect(string effect) { }
        public void LoadUri(System.Uri uri) { } public void LoadString(string html) { }
        public System.Threading.Tasks.Task SendMessageAsync(string message) => System.Threading.Tasks.Task.CompletedTask;
        public void Close() { }
    }
}
