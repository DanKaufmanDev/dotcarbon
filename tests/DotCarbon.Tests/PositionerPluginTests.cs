using System.Text.Json;
using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Positioner;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 6.5: the positioner plugin moves a window to a named spot on its monitor's work area. The math
/// is a pure function, so these pin each position against a known monitor and window size.
/// </summary>
public class PositionerPluginTests
{
    // A 1920x1080 monitor whose work area fills it, with a 400x300 window.
    private static readonly CarbonMonitorInfo Monitor = new(null, 0, 0, 1920, 1080, 0, 0, 1920, 1080, 1.0);

    private static (int X, int Y) At(string position) => PositionerPlugin.ComputePosition(position, Monitor, 400, 300);

    [Fact]
    public void Corners_and_center_land_where_expected()
    {
        Assert.Equal((0, 0), At("TopLeft"));
        Assert.Equal((1520, 0), At("TopRight"));       // 1920 - 400
        Assert.Equal((0, 780), At("BottomLeft"));      // 1080 - 300
        Assert.Equal((1520, 780), At("BottomRight"));
        Assert.Equal((760, 390), At("Center"));        // ((1920-400)/2, (1080-300)/2)
        Assert.Equal((760, 0), At("TopCenter"));
        Assert.Equal((760, 780), At("BottomCenter"));
        Assert.Equal((0, 390), At("LeftCenter"));
        Assert.Equal((1520, 390), At("RightCenter"));
    }

    [Fact]
    public void Work_area_offset_is_respected()
    {
        // A menu bar shrinks the usable area: work area starts at y=25 and is 1055 tall.
        var monitor = new CarbonMonitorInfo(null, 0, 0, 1920, 1080, 0, 25, 1920, 1055, 1.0);

        Assert.Equal((0, 25), PositionerPlugin.ComputePosition("TopLeft", monitor, 400, 300));
        Assert.Equal((1520, 780), PositionerPlugin.ComputePosition("BottomRight", monitor, 400, 300)); // 25 + 1055 - 300
    }

    [Fact]
    public void Position_name_is_case_and_separator_insensitive()
    {
        Assert.Equal(At("TopRight"), At("top-right"));
        Assert.Equal(At("TopRight"), At("TOP_RIGHT"));
        Assert.Equal(At("TopRight"), At("topRight"));
    }

    [Fact]
    public void Unknown_position_throws()
    {
        Assert.Throws<ArgumentException>(() => At("middle-of-nowhere"));
    }

    [Fact]
    public void Move_positions_the_current_window()
    {
        var host = new RecordingHost();
        var config = new CarbonConfig { Window = new WindowConfig { Label = "main" } };
        var app = CarbonApp.Create(config).UsePlatform(host);
        var handle = app.Start();
        try
        {
            // NoopWebView reports an 800x600 monitor and window, so any position resolves to (0,0);
            // this proves the plugin resolves the window, reads monitor + size, and calls SetPosition.
            new PositionerPlugin(handle).Move(new PositionerMoveArgs("Center"));

            Assert.Equal((0, 0), host.Views["main"].LastPosition);
        }
        finally { app.Shutdown(); }
    }

    [Fact]
    public void Registers_its_command()
    {
        var config = new CarbonConfig { Window = new WindowConfig { Label = "main" } };
        var app = CarbonApp.Create(config).UsePlatform(new NoopHost());
        var handle = app.Start();
        try
        {
            var registry = new FakeRegistry();
            new PositionerPlugin(handle).Register(registry);
            Assert.Contains("positioner:move", registry.Handlers.Keys);
        }
        finally { app.Shutdown(); }
    }

    private sealed class FakeRegistry : ICommandRegistry
    {
        public Dictionary<string, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>>> Handlers { get; } =
            new(StringComparer.Ordinal);
        public void Add(string name, Func<JsonElement, Task<System.Text.Json.Nodes.JsonNode?>> handler) =>
            Handlers[name] = handler;
    }
}
