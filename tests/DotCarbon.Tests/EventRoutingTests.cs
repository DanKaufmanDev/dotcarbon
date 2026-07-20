using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.9: emitTo / EventTarget routing. An event's target decides who receives it — a single
/// window's webview, every webview, or only backend listeners — so these drive the real emit path and
/// assert exactly which windows and which backend listeners fire.
/// </summary>
public class EventRoutingTests
{
    private static CarbonConfig Config() => new() { Window = new WindowConfig { Label = "main" } };

    private static (RecordingHost host, AppHandle handle, CarbonApp app) StartTwoWindows()
    {
        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        handle.CreateWindow("second");
        // Ignore anything sent during startup/load; the tests only care about the emit under test.
        host.Views["main"].Sent.Clear();
        host.Views["second"].Sent.Clear();
        return (host, handle, app);
    }

    private static Task Emit(AppHandle handle, CarbonEventTarget target) =>
        handle.Events.EmitJsonAsync("ping", JsonValue.Create(1), sourceWindowLabel: null, target);

    [Fact]
    public async Task Window_target_reaches_only_that_window_and_no_backend_listener()
    {
        var (host, handle, app) = StartTwoWindows();
        var backend = 0;
        handle.Events.Listen(new CarbonEventName<int>("ping"), _ => backend++);
        try
        {
            await Emit(handle, CarbonEventTarget.Window("second"));

            Assert.Empty(host.Views["main"].Sent);
            Assert.Single(host.Views["second"].Sent);
            Assert.Contains("\"event\":\"ping\"", host.Views["second"].Sent[0]);
            Assert.Equal(0, backend); // a window target is for that webview, not backend listeners
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task All_target_reaches_every_window_and_backend_listeners()
    {
        var (host, handle, app) = StartTwoWindows();
        var backend = 0;
        handle.Events.Listen(new CarbonEventName<int>("ping"), _ => backend++);
        try
        {
            await Emit(handle, CarbonEventTarget.All);

            Assert.Single(host.Views["main"].Sent);
            Assert.Single(host.Views["second"].Sent);
            Assert.Equal(1, backend);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task App_target_reaches_backend_listeners_only_no_webview()
    {
        var (host, handle, app) = StartTwoWindows();
        var backend = 0;
        handle.Events.Listen(new CarbonEventName<int>("ping"), _ => backend++);
        try
        {
            await Emit(handle, CarbonEventTarget.App);

            Assert.Empty(host.Views["main"].Sent);
            Assert.Empty(host.Views["second"].Sent);
            Assert.Equal(1, backend);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Window_target_carries_the_source_window_label()
    {
        var (host, handle, app) = StartTwoWindows();
        try
        {
            // A frontend emit records which window emitted it; a listener elsewhere can see the source.
            await handle.Events.EmitJsonAsync("ping", JsonValue.Create(1), "main", CarbonEventTarget.Window("second"));

            var message = JsonNode.Parse(host.Views["second"].Sent[0])!;
            Assert.Equal("main", message["source"]!.GetValue<string>());
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Unknown_window_target_throws()
    {
        var (_, handle, app) = StartTwoWindows();
        try
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(
                () => Emit(handle, CarbonEventTarget.Window("does-not-exist")));
        }
        finally { app.Shutdown(); }
    }
}
