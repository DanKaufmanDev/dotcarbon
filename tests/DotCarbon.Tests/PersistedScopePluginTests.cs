using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.FileSystem;
using DotCarbon.Plugins.PersistedScope;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.9: scope roots granted at runtime are remembered across restarts. These pin the runtime scope
/// itself, that grants persist and restore, and that a grant actually unlocks the fs plugin.
/// </summary>
public class PersistedScopePluginTests : IDisposable
{
    private readonly string _dir;
    private readonly string _store;

    public PersistedScopePluginTests()
    {
        CarbonRuntimeScope.Clear(); // it's a process-global; start clean
        _dir = Directory.CreateTempSubdirectory("carbon-scope-").FullName;
        _store = Path.Combine(_dir, "persisted-scope.json");
    }

    public void Dispose()
    {
        CarbonRuntimeScope.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static CarbonConfig Config() => new()
    {
        Window = new WindowConfig { Label = "main" },
        App = new AppConfig { Identifier = "com.test.scope" },
    };

    private async Task<PersistedScopePlugin> Build(AppHandle handle)
    {
        var plugin = new PersistedScopePlugin(handle);
        await plugin.InitializeAsync(new PluginContext(handle, JsonSerializer.SerializeToElement(new { file = _store })));
        return plugin;
    }

    [Fact]
    public void Runtime_scope_allows_paths_within_a_granted_root_only()
    {
        CarbonRuntimeScope.Allow(CarbonRuntimeScope.FileSystem, _dir);

        Assert.True(CarbonRuntimeScope.IsAllowed("fs", Path.Combine(_dir, "sub", "file.txt")));
        Assert.False(CarbonRuntimeScope.IsAllowed("fs", Path.Combine(Path.GetTempPath(), "elsewhere.txt")));
        Assert.False(CarbonRuntimeScope.IsAllowed("asset", Path.Combine(_dir, "file.txt"))); // scope is separate
    }

    [Fact]
    public async Task Grants_persist_and_restore_across_instances()
    {
        var granted = Path.Combine(_dir, "granted");
        Directory.CreateDirectory(granted);

        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var first = await Build(handle);
            first.Allow(new ScopeGrant("fs", granted));

            // Simulate a restart: forget the in-memory runtime scope, then a fresh plugin loads the store.
            CarbonRuntimeScope.Clear();
            Assert.False(CarbonRuntimeScope.IsAllowed("fs", Path.Combine(granted, "x")));

            var second = await Build(handle);
            Assert.True(CarbonRuntimeScope.IsAllowed("fs", Path.Combine(granted, "x")));
            Assert.Contains(Path.GetFullPath(granted), second.List()["fs"]);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Granted_scope_unlocks_the_fs_plugin()
    {
        var granted = Path.Combine(_dir, "docs");
        Directory.CreateDirectory(granted);
        var file = Path.Combine(granted, "note.txt");
        await File.WriteAllTextAsync(file, "secret");

        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var fs = new FileSystemPlugin(handle); // no configured scopes

            // Without a grant, the fs plugin denies the path.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fs.ReadFile(new ReadFileArgs(file)));

            var persisted = await Build(handle);
            persisted.Allow(new ScopeGrant("fs", granted));

            // Now the runtime grant makes it readable.
            Assert.Equal("secret", await fs.ReadFile(new ReadFileArgs(file)));
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public async Task Registers_its_commands()
    {
        var app = CarbonApp.Create(Config()).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var registry = new FakeRegistry();
            (await Build(handle)).Register(registry);

            Assert.Contains("persisted-scope:allow", registry.Handlers.Keys);
            Assert.Contains("persisted-scope:list", registry.Handlers.Keys);
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
