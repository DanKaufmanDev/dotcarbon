using DotCarbon.Cli.Commands;
using System.Text.Json.Nodes;
using Xunit;

namespace DotCarbon.Tests;

public class CapabilitiesCommandTests
{
    [Fact]
    public void Check_accepts_permission_aliases()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main", "capabilities": ["main"] }
            }
            """,
            "main",
            """
            {
              "windows": ["main"],
              "permissions": ["app:greet", "core:event_emit"]
            }
            """);

        var result = CapabilitiesCommand.Check(dir);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Check_reports_missing_referenced_capability()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main", "capabilities": ["missing"] }
            }
            """);

        var result = CapabilitiesCommand.Check(dir);

        Assert.Contains(result.Errors, error => error.Contains("missing"));
    }

    [Fact]
    public void Check_reports_unknown_target_window()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main", "capabilities": ["main"] }
            }
            """,
            "main",
            """
            {
              "windows": ["settings"],
              "commands": ["app:greet"]
            }
            """);

        var result = CapabilitiesCommand.Check(dir);

        Assert.Contains(result.Errors, error => error.Contains("settings"));
    }

    [Fact]
    public void Check_reports_invalid_command_patterns()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main", "capabilities": ["main"] }
            }
            """,
            "main",
            """
            {
              "windows": ["main"],
              "commands": ["not a command"]
            }
            """);

        var result = CapabilitiesCommand.Check(dir);

        Assert.Contains(result.Errors, error => error.Contains("invalid command pattern"));
    }

    [Fact]
    public void AddPermission_writes_permission_and_attaches_main_window()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main" },
              "security": { "enabled": false }
            }
            """);

        var result = CapabilitiesCommand.AddPermission(dir, "fs");

        Assert.True(result.Added);
        Assert.Equal("fs:default", result.PermissionId);

        var capability = JsonNode.Parse(File.ReadAllText(result.CapabilityPath))!.AsObject();
        Assert.Contains(capability["permissions"]!.AsArray(), value => value!.GetValue<string>() == "fs:default");
        Assert.Contains(capability["windows"]!.AsArray(), value => value!.GetValue<string>() == "main");

        var carbon = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "carbon.json")))!.AsObject();
        Assert.True(carbon["security"]!["enabled"]!.GetValue<bool>());
        Assert.Contains(carbon["window"]!["capabilities"]!.AsArray(), value => value!.GetValue<string>() == "main");
    }

    [Fact]
    public void AddPermission_is_idempotent()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main" }
            }
            """);

        CapabilitiesCommand.AddPermission(dir, "store");
        var result = CapabilitiesCommand.AddPermission(dir, "store:default");

        Assert.False(result.Added);

        var capability = JsonNode.Parse(File.ReadAllText(result.CapabilityPath))!.AsObject();
        Assert.Single(capability["permissions"]!.AsArray());
    }

    [Fact]
    public void Check_warns_when_known_permission_requires_missing_scope()
    {
        var dir = TempProject(
            """
            {
              "app": { "name": "Caps", "version": "0.1.0", "identifier": "dev.dotcarbon.caps" },
              "window": { "label": "main", "capabilities": ["main"] }
            }
            """,
            "main",
            """
            {
              "windows": ["main"],
              "permissions": ["fs:default"]
            }
            """);

        var result = CapabilitiesCommand.Check(dir);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, warning => warning.Contains("plugins.fs.scopes"));
    }

    private static string TempProject(string carbonJson, string? capabilityName = null, string? capabilityJson = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-caps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "carbon.json"), carbonJson);

        if (capabilityName is not null && capabilityJson is not null)
        {
            var capabilityDir = Path.Combine(dir, "src-carbon", "capabilities");
            Directory.CreateDirectory(capabilityDir);
            File.WriteAllText(Path.Combine(capabilityDir, capabilityName + ".json"), capabilityJson);
        }

        return dir;
    }
}
