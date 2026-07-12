using DotCarbon.Cli.Commands;
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
