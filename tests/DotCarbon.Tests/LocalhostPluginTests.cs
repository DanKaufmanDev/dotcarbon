using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Plugins.Localhost;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.10: the localhost plugin serves the app's assets over a real http://127.0.0.1 origin. This
/// drives a real HTTP GET, reusing the convertFileSrc asset route to serve a scoped file end-to-end.
/// </summary>
public class LocalhostPluginTests : IDisposable
{
    private readonly string _dir;

    public LocalhostPluginTests() => _dir = Directory.CreateTempSubdirectory("carbon-localhost-").FullName;

    public void Dispose()
    {
        CarbonAssetScope.Configure([]); // it's a process-global; reset it
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static async Task<LocalhostPlugin> Build()
    {
        var plugin = new LocalhostPlugin();
        await plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new { port = 0 })));
        return plugin;
    }

    [Fact]
    public async Task Serves_a_scoped_file_over_http()
    {
        // Allow the temp dir for convertFileSrc, and put a file there.
        CarbonAssetScope.Configure([_dir]);
        var file = Path.Combine(_dir, "note.txt");
        await File.WriteAllTextAsync(file, "over-http");

        var plugin = await Build();
        try
        {
            var baseUrl = plugin.Url();
            Assert.StartsWith("http://127.0.0.1:", baseUrl);

            using var http = new HttpClient();
            var assetUrl = $"{baseUrl}/__asset__/{Uri.EscapeDataString(file)}";
            var response = await http.GetAsync(assetUrl);

            Assert.True(response.IsSuccessStatusCode, $"status {(int)response.StatusCode}");
            Assert.Equal("over-http", await response.Content.ReadAsStringAsync());
        }
        finally { await plugin.DisposeAsync(); }
    }

    [Fact]
    public async Task Out_of_scope_file_is_not_served()
    {
        CarbonAssetScope.Configure([_dir]);
        var outside = Path.Combine(Path.GetTempPath(), "carbon-localhost-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "secret");
        try
        {
            var plugin = await Build();
            try
            {
                using var http = new HttpClient();
                var response = await http.GetAsync($"{plugin.Url()}/__asset__/{Uri.EscapeDataString(outside)}");
                // The asset pipeline returns "Forbidden" text for a file outside the scope.
                Assert.Equal("Forbidden", await response.Content.ReadAsStringAsync());
            }
            finally { await plugin.DisposeAsync(); }
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public async Task Stop_shuts_the_server_down()
    {
        var plugin = await Build();
        var baseUrl = plugin.Url();
        plugin.Stop();

        Assert.Equal(string.Empty, plugin.Url());

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(() => http.GetAsync($"{baseUrl}/index.html"));
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new LocalhostPlugin().Register(registry);

        Assert.Contains("localhost:url", registry.Handlers.Keys);
        Assert.Contains("localhost:stop", registry.Handlers.Keys);
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
