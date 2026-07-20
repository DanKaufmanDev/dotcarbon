using System.Text.Json;
using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.5: a third-party plugin ships a permission manifest under (src-carbon/)permissions/*.json.
/// `carbon capabilities` discovers it, so its permissions and scope requirements are known without a
/// hardcoded catalog entry.
/// </summary>
public class PermissionManifestTests : IDisposable
{
    private readonly string _root;

    public PermissionManifestTests()
    {
        _root = Directory.CreateTempSubdirectory("carbon-manifest-").FullName;
        var dir = Path.Combine(_root, "src-carbon", "permissions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "myplugin.json"),
            """
            {
                "namespace": "myplugin",
                "package": "Acme.MyPlugin",
                "platforms": ["desktop"],
                "permissions": [
                    {
                        "identifier": "myplugin:default",
                        "description": "All myplugin commands.",
                        "commands": ["myplugin:*"],
                        "requirements": [{ "path": "plugins.myplugin.apiKey", "hint": "set plugins.myplugin.apiKey" }]
                    },
                    { "identifier": "myplugin:allow-foo", "commands": ["myplugin:foo"] }
                ]
            }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Discover_reads_a_third_party_manifest()
    {
        var discovered = CapabilityPermissionCatalog.Discover(_root);

        var def = Assert.Single(discovered, permission => permission.Id == "myplugin:default");
        Assert.Equal("myplugin", def.PluginNamespace);
        Assert.Equal(["myplugin:*"], def.Commands);
        Assert.Equal("plugins.myplugin.apiKey", Assert.Single(def.Requirements).Path);
        Assert.Contains(discovered, permission => permission.Id == "myplugin:allow-foo");
    }

    [Fact]
    public void ForProject_merges_first_party_and_discovered()
    {
        var catalog = CapabilityPermissionCatalog.ForProject(_root);

        Assert.Contains(catalog, permission => permission.Id == "fs:default");        // first-party
        Assert.Contains(catalog, permission => permission.Id == "myplugin:default");  // discovered
        // The namespace alias resolves a discovered permission just like a first-party one.
        Assert.Equal("myplugin:default", CapabilityPermissionCatalog.Resolve(catalog, "myplugin")?.Id);
    }

    [Fact]
    public void Third_party_requirement_warns_until_configured()
    {
        var catalog = CapabilityPermissionCatalog.ForProject(_root);
        string[] ids = ["myplugin:default"];

        // apiKey unset → the manifest's requirement warns.
        var unconfigured = CapabilityPermissionCatalog.RequirementWarnings(catalog, new CarbonConfig(), ids);
        Assert.Contains(unconfigured, warning => warning.Contains("plugins.myplugin.apiKey"));

        // apiKey set → no warning.
        var config = new CarbonConfig();
        config.Plugins["myplugin"] = JsonSerializer.SerializeToElement(new { apiKey = "secret" });
        Assert.Empty(CapabilityPermissionCatalog.RequirementWarnings(catalog, config, ids));
    }
}
