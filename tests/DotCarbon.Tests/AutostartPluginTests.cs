using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Autostart;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.3: the autostart plugin registers a per-user login entry. On macOS/Linux that's a file
/// (LaunchAgent plist / .desktop), so these drive enable/disable/is_enabled against a temp entry path.
/// </summary>
public class AutostartPluginTests : IDisposable
{
    private readonly string _dir;
    private readonly string _entry;

    public AutostartPluginTests()
    {
        _dir = Directory.CreateTempSubdirectory("carbon-autostart-").FullName;
        // The macOS entry is a plist; the exact extension doesn't matter for the file-based test.
        _entry = Path.Combine(_dir, "login", "com.test.autostart.plist");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CarbonConfig Config() => new()
    {
        Window = new WindowConfig { Label = "main" },
        App = new AppConfig { Identifier = "com.test.autostart", Name = "AutoTest" },
    };

    private async Task<AutostartPlugin> Build(AppHandle handle)
    {
        var plugin = new AutostartPlugin(handle);
        await plugin.InitializeAsync(new PluginContext(handle, JsonSerializer.SerializeToElement(new { entryPath = _entry })));
        return plugin;
    }

    [Fact]
    public async Task Enable_then_disable_toggles_the_login_entry()
    {
        // Windows uses the registry, which we don't touch in CI; the file path covers macOS/Linux.
        if (OperatingSystem.IsWindows()) return;

        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle);

            Assert.False(plugin.IsEnabled());

            plugin.Enable();
            Assert.True(plugin.IsEnabled());
            Assert.True(File.Exists(_entry));

            plugin.Disable();
            Assert.False(plugin.IsEnabled());
            Assert.False(File.Exists(_entry));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Enabled_entry_launches_at_load()
    {
        if (OperatingSystem.IsWindows()) return;

        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle);
            plugin.Enable();

            var content = await File.ReadAllTextAsync(_entry);
            if (OperatingSystem.IsMacOS())
            {
                Assert.Contains("<key>RunAtLoad</key>", content);
                Assert.Contains("<string>com.test.autostart</string>", content); // the Label
            }
            else
            {
                Assert.Contains("X-GNOME-Autostart-enabled=true", content);
            }
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
            new AutostartPlugin(handle).Register(registry);

            Assert.Contains("autostart:enable", registry.Handlers.Keys);
            Assert.Contains("autostart:is_enabled", registry.Handlers.Keys);
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
