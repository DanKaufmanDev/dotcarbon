using System.Text.Json;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.FileSystem;
using Xunit;

namespace DotCarbon.Tests;

public class FileSystemPluginTests
{
    // The plugin resolves capability scopes through its AppHandle, so build a real (window-less) app.
    private static FileSystemPlugin Build(out System.Action shutdown, params string[] scopes)
    {
        var config = new CarbonConfig { Window = new WindowConfig { Label = "main" } };
        var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
        var handle = app.Start();
        shutdown = app.Shutdown;

        var plugin = new FileSystemPlugin(handle);
        if (scopes.Length > 0)
            plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new { scopes }))).AsTask().Wait();
        return plugin;
    }

    [Fact]
    public async Task Commands_require_configured_scope()
    {
        var plugin = Build(out var shutdown);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                plugin.Exists(new ExistsArgs(Path.Combine(Path.GetTempPath(), "carbon-no-scope"))));
        }
        finally { shutdown(); }
    }

    [Fact]
    public async Task Commands_use_normalized_paths_within_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "carbon-fs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var plugin = Build(out var shutdown, root);
        try
        {
            var target = Path.Combine(root, "nested", "..", "settings.json");
            await plugin.WriteFile(new WriteFileArgs(target, "{\"ok\":true}"));

            Assert.Equal("{\"ok\":true}", await plugin.ReadFile(new ReadFileArgs(Path.Combine(root, "settings.json"))));
        }
        finally { shutdown(); }
    }

    [Fact]
    public async Task Commands_reject_paths_outside_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "carbon-fs-test-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "carbon-fs-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var plugin = Build(out var shutdown, root);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                plugin.CreateDir(new ReadDirArgs(Path.Combine(outside, "blocked"))));
        }
        finally { shutdown(); }
    }
}
