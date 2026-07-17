using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.1: a command streams several messages to the frontend through a channel it receives in its
/// arguments. The channel is deserialized from the frontend's marker and bound to the calling window,
/// so the messages arrive as channel bridge messages at that webview, in order.
/// </summary>
public class ChannelTests
{
    [Fact]
    public async Task Command_streams_messages_through_a_channel()
    {
        var config = new CarbonConfig { Window = new WindowConfig { Label = "main" } };
        var capture = new CapturingHost();
        var app = CarbonApp.Create(config).UsePlatform(capture).UsePlugin(new StreamPlugin());
        var handle = app.Start();
        try
        {
            var window = handle.GetWindow("main");
            var registry = new FakeRegistry();
            new StreamPlugin().Register(registry);

            // The channel converter reads the ambient invocation to bind the window, exactly as the
            // runtime sets it around a real command.
            CarbonInvocationScope.Current = new CarbonCommandContext(handle, window);
            try
            {
                var payload = JsonDocument.Parse("""{ "onData": { "__carbon_channel__": 42 }, "count": 3 }""").RootElement;
                await registry.Handlers["stream:count"](payload);
            }
            finally
            {
                CarbonInvocationScope.Current = null;
            }

            // Three channel messages, in order, carrying 0/1/2 on channel 42.
            Assert.Equal(3, capture.Channels.Count);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal("channel", capture.Channels[i]["type"]!.GetValue<string>());
                Assert.Equal(42, capture.Channels[i]["id"]!.GetValue<long>());
                Assert.Equal(i, capture.Channels[i]["message"]!.GetValue<int>());
            }
        }
        finally
        {
            app.Shutdown();
        }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<JsonNode?>>> Handlers { get; } = new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler) => Handlers[name] = handler;
    }

    // The Carbon command generator only runs in src/, so Register is written by hand here — which is
    // fine, it exercises the same channel converter + send path a generated command would.
    private sealed class StreamPlugin : IPlugin
    {
        public string Namespace => "stream";

        public void Register(ICommandRegistry registry)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            registry.Add("stream:count", async payload =>
            {
                var args = payload.Deserialize<CountArgs>(options)!;
                for (var i = 0; i < args.Count; i++)
                    await args.OnData.SendAsync(JsonValue.Create(i));
                return null;
            });
        }
    }

    public sealed record CountArgs(CarbonChannel OnData, int Count);

    // A host whose webview records every bridge message sent to it, so channel messages can be inspected.
    private sealed class CapturingHost : ICarbonPlatformHost
    {
        public List<JsonObject> Channels { get; } = new();
        public ICarbonWebView CreateWebView(CarbonWebViewContext context) => new CapturingWebView(this);
        public void Run(ICarbonWebView mainWebView) { }
    }

    private sealed class CapturingWebView(CapturingHost host) : ICarbonWebView
    {
        public Task SendMessageAsync(string message)
        {
            var node = JsonNode.Parse(message)!.AsObject();
            if (node["type"]?.GetValue<string>() == "channel") host.Channels.Add(node);
            return Task.CompletedTask;
        }

        public string Title => "main";
        public int Width => 800; public int Height => 600; public int X => 0; public int Y => 0;
        public bool IsFullscreen => false; public bool IsMaximized => false; public bool IsMinimized => false;
        public bool IsAlwaysOnTop => false; public bool IsResizable => true; public bool IsVisible => true;
        public bool IsFocused => false;
        public void SetTitle(string title) { } public void SetSize(int w, int h) { } public void SetPosition(int x, int y) { }
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
        public void Close() { }
    }
}
