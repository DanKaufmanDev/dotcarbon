using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Path;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.3: the path plugin resolves OS-standard app directories under the app identifier, and its
/// path ops use the running OS's rules.
/// </summary>
public class PathPluginTests
{
    private static PathPlugin Build(out System.Action shutdown)
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig { Label = "main" },
            App = new AppConfig { Identifier = "dev.carbon.pathtest" },
        };
        var app = CarbonApp.Create(config).UsePlatform(new PathHost());
        var handle = app.Start();
        shutdown = app.Shutdown;
        return new PathPlugin(handle);
    }

    [Fact]
    public void App_dirs_are_absolute_and_carry_the_identifier()
    {
        var path = Build(out var shutdown);
        try
        {
            foreach (var dir in new[] { path.AppDataDir(), path.AppConfigDir(), path.AppCacheDir(), path.AppLogDir() })
            {
                Assert.True(System.IO.Path.IsPathFullyQualified(dir), dir);
                Assert.Contains("dev.carbon.pathtest", dir);
            }
            Assert.True(System.IO.Path.IsPathFullyQualified(path.HomeDir()));
            Assert.True(System.IO.Path.IsPathFullyQualified(path.TempDir()));
        }
        finally { shutdown(); }
    }

    [Fact]
    public void Path_ops_follow_the_os()
    {
        var path = Build(out var shutdown);
        try
        {
            Assert.Equal("file.txt", path.Basename(new PathArg("/a/b/file.txt")));
            Assert.Equal(".txt", path.Extname(new PathArg("/a/b/file.txt")));
            Assert.Equal(System.IO.Path.DirectorySeparatorChar.ToString(), path.Sep());
            Assert.True(path.IsAbsolute(new PathArg(System.IO.Path.GetTempPath())));
            Assert.False(path.IsAbsolute(new PathArg("relative/dir")));

            var joined = path.Join(new PathPartsArgs(["a", "b", "c"]));
            Assert.Equal(System.IO.Path.Combine("a", "b", "c"), joined);
        }
        finally { shutdown(); }
    }

    private sealed class PathHost : ICarbonPlatformHost
    {
        public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new StubView();
        public void Run(ICarbonWebView mainWebView) { }
    }

    private sealed class StubView : ICarbonWebView
    {
        public string Title => "main";
        public int Width => 800; public int Height => 600; public int X => 0; public int Y => 0;
        public bool IsFullscreen => false; public bool IsMaximized => false; public bool IsMinimized => false;
        public bool IsAlwaysOnTop => false; public bool IsResizable => true; public bool IsVisible => true;
        public bool IsFocused => false;
        public void SetTitle(string t) { } public void SetSize(int w, int h) { } public void SetPosition(int x, int y) { }
        public void Center() { } public void SetMinSize(int w, int h) { } public void SetMaxSize(int w, int h) { }
        public (int, int) GetInnerSize() => (800, 600); public (int, int) GetOuterSize() => (800, 600);
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
