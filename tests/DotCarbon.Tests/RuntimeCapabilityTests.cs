using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCarbon.Tests;

public class RuntimeCapabilityTests
{
    [Fact]
    public void Runtime_expands_plugin_permission_metadata()
    {
        var config = new CarbonConfig
        {
            Window = new WindowConfig
            {
                Label = "main",
                Capabilities = ["main"],
            },
            Security = new SecurityConfig
            {
                Enabled = true,
                Capabilities =
                {
                    ["main"] = new CapabilityConfig
                    {
                        Permissions = [new PermissionEntry { Identifier = "test:default" }],
                    },
                },
            },
        };

        var app = CarbonApp.Create(config)
            .UsePlatform(new TestHost())
            .UsePlugin(new TestPlugin());

        var handle = app.Start();
        try
        {
            var capabilities = handle.Services.GetRequiredService<CapabilityManager>();
            var window = handle.GetWindow("main");

            capabilities.EnsureCommandAllowed(window, "test:ping");
            Assert.Throws<UnauthorizedAccessException>(
                () => capabilities.EnsureCommandAllowed(window, "other:ping"));
        }
        finally
        {
            app.Shutdown();
        }
    }

    [CarbonPermission("test:default", "Allow test commands.", Commands = new[] { "test:*" })]
    private sealed class TestPlugin : IPlugin
    {
        public string Namespace => "test";

        public void Register(ICommandRegistry registry)
        {
        }
    }

    private sealed class TestHost : ICarbonPlatformHost
    {
        public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new TestWebView(context.Options);

        public void Run(ICarbonWebView mainWebView)
        {
        }
    }

    private sealed class TestWebView(CarbonWindowOptions options) : ICarbonWebView
    {
        public string Title { get; private set; } = options.Title;
        public int Width { get; private set; } = options.Width;
        public int Height { get; private set; } = options.Height;
        public int X { get; private set; } = options.X ?? 0;
        public int Y { get; private set; } = options.Y ?? 0;
        public bool IsFullscreen { get; private set; } = options.Fullscreen;
        public bool IsMaximized { get; private set; } = options.Maximized;
        public bool IsMinimized { get; private set; }
        public bool IsAlwaysOnTop { get; private set; } = options.AlwaysOnTop;
        public bool IsResizable { get; private set; } = options.Resizable;
        public bool IsVisible { get; private set; } = true;
        public bool IsFocused { get; private set; }

        public void SetTitle(string title) => Title = title;
        public void SetSize(int width, int height) => (Width, Height) = (width, height);
        public void SetPosition(int x, int y) => (X, Y) = (x, y);
        public void Center() => (X, Y) = (0, 0);
        public void SetMinimized(bool minimized) => IsMinimized = minimized;
        public void SetMaximized(bool maximized) => IsMaximized = maximized;
        public void SetFullscreen(bool fullscreen) => IsFullscreen = fullscreen;
        public void SetAlwaysOnTop(bool alwaysOnTop) => IsAlwaysOnTop = alwaysOnTop;
        public void SetResizable(bool resizable) => IsResizable = resizable;
        public void SetMinSize(int width, int height) { }
        public void SetMaxSize(int width, int height) { }
        public (int, int) GetInnerSize() => (Width, Height);
        public (int, int) GetOuterSize() => (Width, Height);
        public (int, int) GetInnerPosition() => (X, Y);
        public (int, int) GetOuterPosition() => (X, Y);
        public void Show() => (IsVisible, IsFocused) = (true, true);
        public void Hide() => (IsVisible, IsFocused) = (false, false);
        public void SetFocus() => IsFocused = true;
        public void RequestUserAttention() { }
        public void StartDragging() { }
        public void SetDecorations(bool decorations) { }
        public void SetClosable(bool closable) { }
        public void SetMinimizable(bool minimizable) { }
        public void SetMaximizable(bool maximizable) { }
        public void SetAlwaysOnBottom(bool alwaysOnBottom) { }
        public void SetSkipTaskbar(bool skip) { }
        public void SetContentProtected(bool protectedContent) { }
        public void SetIgnoreCursorEvents(bool ignore) { }
        public void SetIcon(string path) { }
        public void SetCursorIcon(string icon) { }
        public void SetCursorVisible(bool visible) { }
        public void SetCursorGrab(bool grab) { }
        public void SetCursorPosition(int x, int y) { }
        public IReadOnlyList<CarbonMonitorInfo> GetMonitors() => [GetPrimaryMonitor()!];
        public CarbonMonitorInfo? GetPrimaryMonitor() => new(null, 0, 0, Width, Height, 0, 0, Width, Height, 1.0);
        public CarbonMonitorInfo? GetCurrentMonitor() => GetPrimaryMonitor();
        public double GetScaleFactor() => 1.0;
        public string GetTheme() => "light";
        public void SetTheme(string theme) { }
        public void SetProgressBar(string status, int progress) { }
        public void SetBadge(string? label) { }
        public void SetEffect(string effect) { }
        public void LoadUri(Uri uri) { }
        public void LoadString(string html) { }
        public Task SendMessageAsync(string message) => Task.CompletedTask;
        public void Close() { }
    }
}
