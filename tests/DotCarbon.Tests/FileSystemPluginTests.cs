using System.Text.Json;
using DotCarbon.Core.Plugins;
using DotCarbon.Plugins.FileSystem;
using Xunit;

namespace DotCarbon.Tests;

public class FileSystemPluginTests
{
    [Fact]
    public async Task Commands_require_configured_scope()
    {
        var plugin = new FileSystemPlugin();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            plugin.Exists(new ExistsArgs(Path.Combine(Path.GetTempPath(), "carbon-no-scope"))));
    }

    [Fact]
    public async Task Commands_use_normalized_paths_within_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "carbon-fs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var plugin = new FileSystemPlugin();
        await plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new
        {
            scopes = new[] { root }
        })));

        var target = Path.Combine(root, "nested", "..", "settings.json");
        await plugin.WriteFile(new WriteFileArgs(target, "{\"ok\":true}"));

        Assert.Equal("{\"ok\":true}", await plugin.ReadFile(new ReadFileArgs(Path.Combine(root, "settings.json"))));
    }

    [Fact]
    public async Task Commands_reject_paths_outside_scope()
    {
        var root = Path.Combine(Path.GetTempPath(), "carbon-fs-test-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "carbon-fs-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var plugin = new FileSystemPlugin();
        await plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new
        {
            scopes = new[] { root }
        })));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            plugin.CreateDir(new ReadDirArgs(Path.Combine(outside, "blocked"))));
    }
}
