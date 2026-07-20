using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Log;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.1: the log plugin writes leveled records to the configured targets (stdout, a rolling file,
/// the webview console) and drops anything below the minimum level.
/// </summary>
public class LogPluginTests : IDisposable
{
    private readonly string _dir;

    public LogPluginTests() => _dir = Directory.CreateTempSubdirectory("carbon-log-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CarbonConfig Config() => new()
    {
        Window = new WindowConfig { Label = "main" },
        App = new AppConfig { Identifier = "com.test.log", Name = "LogTest" },
    };

    private static async Task<LogPlugin> Build(AppHandle handle, object options)
    {
        var plugin = new LogPlugin();
        await plugin.InitializeAsync(new PluginContext(handle, JsonSerializer.SerializeToElement(options)));
        return plugin;
    }

    [Fact]
    public async Task File_target_writes_a_formatted_line()
    {
        var logPath = Path.Combine(_dir, "app.log");
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle, new { targets = new[] { "file" }, level = "trace", file = logPath });
            await plugin.Info("hello world");

            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("[INFO]", content);
            Assert.Contains("hello world", content);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Level_below_the_minimum_is_dropped()
    {
        var logPath = Path.Combine(_dir, "filtered.log");
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle, new { targets = new[] { "file" }, level = "warn", file = logPath });

            await plugin.Info("dropped");
            Assert.False(File.Exists(logPath)); // below the minimum → nothing written

            await plugin.Error("kept");
            var content = await File.ReadAllTextAsync(logPath);
            Assert.Contains("kept", content);
            Assert.DoesNotContain("dropped", content);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Webview_target_emits_a_log_message_event()
    {
        var host = new RecordingHost();
        var app = CarbonApp.Create(Config()).UsePlatform(host);
        var handle = app.Start();
        try
        {
            var plugin = await Build(handle, new { targets = new[] { "webview" }, level = "trace" });
            host.Views["main"].Sent.Clear();

            await plugin.Warn("to the console");

            Assert.Contains(
                host.Views["main"].Sent,
                message => message.Contains("log:message") && message.Contains("to the console"));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Registers_the_log_command()
    {
        var registry = new FakeRegistry();
        new LogPlugin().Register(registry);

        Assert.Contains("log:log", registry.Handlers.Keys);
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
