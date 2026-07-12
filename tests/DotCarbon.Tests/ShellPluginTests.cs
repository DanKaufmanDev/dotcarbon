using System.Text.Json;
using DotCarbon.Core.Plugins;
using DotCarbon.Plugins.Shell;
using Xunit;

namespace DotCarbon.Tests;

public class ShellPluginTests
{
    [Fact]
    public async Task Execute_rejects_default_denied_environment_variables()
    {
        var plugin = new ShellPlugin();
        await plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new
        {
            allowedPrograms = new[] { "fake-safe-command" }
        })));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            plugin.Execute(new ExecuteArgs(
                "fake-safe-command",
                Env: new Dictionary<string, string> { ["NPM_TOKEN"] = "secret" })));
    }

    [Fact]
    public async Task Execute_rejects_environment_variables_outside_allowlist()
    {
        var plugin = new ShellPlugin();
        await plugin.InitializeAsync(new PluginContext(null!, JsonSerializer.SerializeToElement(new
        {
            allowedPrograms = new[] { "fake-safe-command" },
            allowedEnv = new[] { "CARBON_ALLOWED" }
        })));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            plugin.Execute(new ExecuteArgs(
                "fake-safe-command",
                Env: new Dictionary<string, string> { ["CARBON_BLOCKED"] = "nope" })));
    }
}
