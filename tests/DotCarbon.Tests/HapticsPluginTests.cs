using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Haptics;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.5: haptics is the first mobile-only plugin. The command surface normalizes input and routes
/// to an IHapticsProvider; the Android/iOS providers (DotCarbon.Plugins.Haptics.Native) are verified by
/// building the mobile TFMs and on a device. Desktop is a deliberate no-op.
/// </summary>
public class HapticsPluginTests
{
    [Fact]
    public async Task Commands_normalize_and_route_to_the_provider()
    {
        var provider = new FakeHaptics();
        var plugin = new HapticsPlugin(provider);

        await plugin.Impact(new ImpactArgs("  HEAVY "));
        await plugin.Notification(new NotificationArgs("Error"));
        await plugin.Vibrate(new VibrateArgs(250));

        Assert.Equal("heavy", provider.LastImpact);
        Assert.Equal("error", provider.LastNotification);
        Assert.Equal(250, provider.LastDuration);
    }

    [Theory]
    [InlineData(null, "medium")]
    [InlineData("", "medium")]
    [InlineData("   ", "medium")]
    [InlineData("Light", "light")]
    public void Blank_styles_fall_back(string? style, string expected) =>
        Assert.Equal(expected, HapticsPlugin.Normalize(style, "medium"));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-50, 1)]
    [InlineData(100, 100)]
    [InlineData(99999, HapticsPlugin.MaxDurationMs)]
    public void Durations_are_clamped(int input, int expected) =>
        Assert.Equal(expected, HapticsPlugin.ClampDuration(input));

    [Fact]
    public async Task Desktop_falls_back_to_a_no_op()
    {
        // The no-op provider must succeed so shared frontend code can call haptics unconditionally.
        var plugin = new HapticsPlugin(new NoopHapticsProvider());

        await plugin.Impact(new ImpactArgs("heavy"));
        await plugin.Notification(new NotificationArgs("error"));
        await plugin.Vibrate(new VibrateArgs(100));
    }

    [Fact]
    public void Registers_its_commands()
    {
        var registry = new FakeRegistry();
        new HapticsPlugin(new FakeHaptics()).Register(registry);

        Assert.Contains("haptics:impact", registry.Handlers.Keys);
        Assert.Contains("haptics:notification", registry.Handlers.Keys);
        Assert.Contains("haptics:vibrate", registry.Handlers.Keys);
    }

    private sealed class FakeHaptics : IHapticsProvider
    {
        public string? LastImpact { get; private set; }
        public string? LastNotification { get; private set; }
        public int LastDuration { get; private set; }

        public Task ImpactAsync(string style) { LastImpact = style; return Task.CompletedTask; }
        public Task NotificationAsync(string type) { LastNotification = type; return Task.CompletedTask; }
        public Task VibrateAsync(int durationMs) { LastDuration = durationMs; return Task.CompletedTask; }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
