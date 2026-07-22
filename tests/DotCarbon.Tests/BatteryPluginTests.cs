using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Battery;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.3: the Battery plugin is the reference for the mobile native-binding pattern. The
/// cross-platform command surface + desktop reader are exercised here; the Android/iOS providers
/// (DotCarbon.Plugins.Battery.Native) are verified by building for the mobile TFMs and on a device.
/// </summary>
public class BatteryPluginTests
{
    [Theory]
    [InlineData("100%; charged; 0:00 remaining present: true", 1.0, false, "full")]
    [InlineData(" -InternalBattery-0 (id=1)\t83%; discharging; 4:12 remaining present: true", 0.83, false, "discharging")]
    [InlineData("45%; charging; 1:23 remaining present: true", 0.45, true, "charging")]
    [InlineData("Now drawing from 'AC Power' — no battery", null, null, "unknown")]
    public void Parses_pmset_output(string output, double? level, bool? charging, string state)
    {
        var status = DesktopBatteryProvider.ParsePmset(output);
        Assert.Equal(level, status.Level);
        Assert.Equal(charging, status.Charging);
        Assert.Equal(state, status.State);
    }

    [Fact]
    public void Status_reads_from_the_registered_provider()
    {
        var plugin = new BatteryPlugin(new FakeProvider(new BatteryStatus(0.5, true, "charging")));

        var status = plugin.Status();

        Assert.Equal(0.5, status.Level);
        Assert.True(status.Charging);
        Assert.Equal("charging", status.State);
    }

    [Fact]
    public void Registers_its_status_command()
    {
        var registry = new FakeRegistry();
        new BatteryPlugin(new FakeProvider(new BatteryStatus(1.0, false, "full"))).Register(registry);

        Assert.Contains("battery:status", registry.Handlers.Keys);
    }

    private sealed class FakeProvider(BatteryStatus status) : IBatteryProvider
    {
        public BatteryStatus Read() => status;
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
