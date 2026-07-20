using System.CommandLine;
using System.Text.Json.Nodes;
using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 5.6: `carbon permission new/ls` and `carbon capability new` scaffolding. These invoke the real
/// commands in-process and assert the files they write are correct and discoverable.
/// </summary>
public class ScaffoldCommandTests : IDisposable
{
    private readonly string _root;

    public ScaffoldCommandTests() => _root = Directory.CreateTempSubdirectory("carbon-scaffold-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Permission_new_writes_a_discoverable_entry_with_a_derived_command()
    {
        var exit = await PermissionCommand.Build().InvokeAsync(
            ["new", "myplugin:allow-sync", "--project", _root, "--description", "Sync data."]);

        Assert.Equal(0, exit);
        var def = Assert.Single(
            CapabilityPermissionCatalog.ForProject(_root),
            permission => permission.Id == "myplugin:allow-sync");
        // "allow-sync" derives the command "myplugin:sync".
        Assert.Equal(["myplugin:sync"], def.Commands);
    }

    [Fact]
    public async Task Permission_new_is_idempotent()
    {
        await PermissionCommand.Build().InvokeAsync(["new", "myplugin:allow-sync", "--project", _root]);
        await PermissionCommand.Build().InvokeAsync(["new", "myplugin:allow-sync", "--project", _root]);

        var manifest = JsonNode.Parse(
            File.ReadAllText(Path.Combine(_root, "src-carbon", "permissions", "myplugin.json")))!;
        Assert.Single(manifest["permissions"]!.AsArray());
    }

    [Fact]
    public async Task Capability_new_creates_the_file_and_attaches_it_to_the_window()
    {
        File.WriteAllText(Path.Combine(_root, "carbon.json"),
            """{ "window": { "label": "main" }, "security": { "enabled": true } }""");

        var exit = await CapabilitiesCommand.Build().InvokeAsync(["new", "settings", "--project", _root]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_root, "src-carbon", "capabilities", "settings.json")));

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "carbon.json")))!;
        var attached = config["window"]!["capabilities"]!.AsArray().Select(node => node!.GetValue<string>());
        Assert.Contains("settings", attached);
    }

    [Fact]
    public async Task Capability_new_does_not_overwrite_without_force()
    {
        var path = Path.Combine(_root, "src-carbon", "capabilities", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"identifier\": \"settings\", \"marker\": true }");

        await CapabilitiesCommand.Build().InvokeAsync(["new", "settings", "--project", _root]);

        // The existing file is left untouched (no --force).
        Assert.Contains("marker", File.ReadAllText(path));
    }
}
