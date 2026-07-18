using System.Text.Json;
using System.Text.Json.Nodes;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Process;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 4.4: the process plugin. exit/relaunch terminate the process, so the honest checks are that
/// the commands register and that pid reports this process.
/// </summary>
public class ProcessPluginTests
{
    [Fact]
    public void Pid_reports_the_current_process()
    {
        Assert.Equal(Environment.ProcessId, new ProcessPlugin().Pid());
    }

    [Fact]
    public void Plugin_registers_its_commands()
    {
        var registry = new FakeRegistry();
        new ProcessPlugin().Register(registry);

        Assert.Equal(
            new[] { "process:exit", "process:pid", "process:relaunch" },
            registry.Handlers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Pid_command_round_trips_through_the_registry()
    {
        var registry = new FakeRegistry();
        new ProcessPlugin().Register(registry);

        var result = await registry.Handlers["process:pid"](default);
        Assert.Equal(Environment.ProcessId, result!.GetValue<int>());
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<JsonNode?>>> Handlers { get; } = new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<JsonNode?>> handler) => Handlers[name] = handler;
    }
}
