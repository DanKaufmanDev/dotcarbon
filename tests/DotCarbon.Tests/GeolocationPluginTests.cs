using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Plugins.Geolocation;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 7.5: geolocation. The command surface clamps the timeout and routes to an IGeolocationProvider;
/// the Android/iOS providers (DotCarbon.Plugins.Geolocation.Native) are verified by building the mobile
/// TFMs and against a mock fix on a device.
/// </summary>
public class GeolocationPluginTests
{
    [Fact]
    public async Task Current_returns_the_providers_fix()
    {
        var fix = new GeolocationPosition(51.5, -0.12, 12.5, 30, 1.4, 1_700_000_000_000);
        var provider = new FakeGeolocation(fix);
        var plugin = new GeolocationPlugin(provider);

        var position = await plugin.Current(new CurrentPositionArgs(5_000));

        Assert.Equal(fix, position);
        Assert.Equal(5_000, provider.LastTimeout);
        // Default: no cached fix is acceptable for a "current position" request.
        Assert.Equal(0, provider.LastMaximumAge);
    }

    [Fact]
    public async Task No_fix_surfaces_as_null_rather_than_an_error()
    {
        var plugin = new GeolocationPlugin(new FakeGeolocation(null));

        Assert.Null(await plugin.Current(new CurrentPositionArgs()));
    }

    [Theory]
    [InlineData(0, GeolocationPlugin.MinTimeoutMs)]
    [InlineData(-1, GeolocationPlugin.MinTimeoutMs)]
    [InlineData(10_000, 10_000)]
    [InlineData(int.MaxValue, GeolocationPlugin.MaxTimeoutMs)]
    public void Timeouts_are_clamped(int input, int expected) =>
        Assert.Equal(expected, GeolocationPlugin.ClampTimeout(input));

    [Fact]
    public async Task Without_a_native_provider_it_reports_clearly_instead_of_guessing()
    {
        // A caller asking "where am I?" must not get a fabricated answer.
        var plugin = new GeolocationPlugin(new UnsupportedGeolocationProvider());

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => plugin.Current(new CurrentPositionArgs()));
        Assert.Contains("UseGeolocation", error.Message);
    }

    [Fact]
    public void Registers_its_command()
    {
        var registry = new FakeRegistry();
        new GeolocationPlugin(new FakeGeolocation(null)).Register(registry);

        Assert.Contains("geolocation:current", registry.Handlers.Keys);
    }

    private sealed class FakeGeolocation : IGeolocationProvider
    {
        private readonly GeolocationPosition? _fix;

        public FakeGeolocation(GeolocationPosition? fix) => _fix = fix;

        public int LastTimeout { get; private set; }
        public int LastMaximumAge { get; private set; }

        public Task<GeolocationPosition?> GetCurrentAsync(int timeoutMs, int maximumAgeMs)
        {
            LastTimeout = timeoutMs;
            LastMaximumAge = maximumAgeMs;
            return Task.FromResult(_fix);
        }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
