using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.WindowState;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.2: the window-state plugin persists each window's geometry and re-applies it on the next run.
/// </summary>
public class WindowStatePluginTests : IDisposable
{
    private readonly string _dir;
    private readonly string _stateFile;

    public WindowStatePluginTests()
    {
        _dir = Directory.CreateTempSubdirectory("carbon-winstate-").FullName;
        _stateFile = Path.Combine(_dir, "window-state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CarbonConfig Config() => new()
    {
        Window = new WindowConfig { Label = "main" },
        App = new AppConfig { Identifier = "com.test.winstate", Name = "WinState" },
    };

    private async Task<WindowStatePlugin> Build(AppHandle handle)
    {
        var plugin = new WindowStatePlugin(handle);
        await plugin.InitializeAsync(new PluginContext(handle, JsonSerializer.SerializeToElement(new { file = _stateFile })));
        return plugin;
    }

    private void SeedState(string label, WindowState state)
    {
        var seed = new Dictionary<string, WindowState> { [label] = state };
        File.WriteAllText(_stateFile,
            JsonSerializer.Serialize(seed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public async Task Saved_state_round_trips_across_plugin_instances()
    {
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            // First run captures the main window (NoopWebView reports 800x600 at 0,0) and writes the file.
            var first = await Build(handle);
            first.SaveState();

            // A fresh instance loads it back.
            var second = await Build(handle);
            var restored = second.GetState(new WindowLabelArgs("main"));

            Assert.Equal(new WindowState(800, 600, 0, 0, false), restored);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Restore_applies_saved_size_and_position()
    {
        SeedState("main", new WindowState(1024, 768, 50, 60, Maximized: false));

        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        try
        {
            // InitializeAsync restores existing windows, so the main window's setters fire.
            await Build(handle);

            Assert.Equal((1024, 768), host.Views["main"].LastSize);
            Assert.Equal((50, 60), host.Views["main"].LastPosition);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Maximized_state_restores_maximized_not_size()
    {
        SeedState("main", new WindowState(1024, 768, 50, 60, Maximized: true));

        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        try
        {
            await Build(handle);

            Assert.Equal(true, host.Views["main"].LastMaximized);
            Assert.Null(host.Views["main"].LastSize); // a maximized window isn't sized/positioned
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Clear_removes_the_state_file()
    {
        SeedState("main", new WindowState(1, 2, 3, 4, false));
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle);
            Assert.True(File.Exists(_stateFile));

            plugin.ClearState();

            Assert.False(File.Exists(_stateFile));
            Assert.Null(plugin.GetState(new WindowLabelArgs("main")));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Registers_its_commands()
    {
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var registry = new FakeRegistry();
            new WindowStatePlugin(handle).Register(registry);

            Assert.Contains("window-state:save", registry.Handlers.Keys);
            Assert.Contains("window-state:restore", registry.Handlers.Keys);
        }
        finally { app.Shutdown(); }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
