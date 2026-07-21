using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Positioner;

/// <summary>
/// Move a window to a named position on its monitor's work area (Task 6.5) — top-right, center, and so
/// on — mirroring Tauri's positioner plugin.
/// </summary>
[CarbonPlugin("Positioner", description: "Move a window to named positions (top-right, center, …).")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("positioner:default", "Allow all positioner commands.", Commands = new[] { "positioner:*" })]
public partial class PositionerPlugin : IPlugin
{
    private readonly AppHandle _app;

    public PositionerPlugin(AppHandle app) => _app = app;

    public string Namespace => "positioner";

    /// <summary>Move a window to a named position.</summary>
    [CarbonCommand("move")]
    public void Move(PositionerMoveArgs args)
    {
        var window = args.Label is { } label && _app.TryGetWindow(label, out var target)
            ? target
            : _app.CurrentWindow;

        var monitor = window.Native.GetCurrentMonitor() ?? window.Native.GetPrimaryMonitor()
            ?? throw new InvalidOperationException("No monitor is available to position against.");
        var (width, height) = window.Native.GetOuterSize();

        var (x, y) = ComputePosition(args.Position, monitor, width, height);
        window.SetPosition(x, y);
    }

    /// <summary>The top-left coordinate for a named position, within the monitor's work area.</summary>
    internal static (int X, int Y) ComputePosition(string position, CarbonMonitorInfo monitor, int width, int height)
    {
        var left = monitor.WorkX;
        var top = monitor.WorkY;
        var right = monitor.WorkX + monitor.WorkWidth - width;
        var bottom = monitor.WorkY + monitor.WorkHeight - height;
        var centerX = monitor.WorkX + (monitor.WorkWidth - width) / 2;
        var centerY = monitor.WorkY + (monitor.WorkHeight - height) / 2;

        return Normalize(position) switch
        {
            "topleft" => (left, top),
            "topright" => (right, top),
            "bottomleft" => (left, bottom),
            "bottomright" => (right, bottom),
            "topcenter" => (centerX, top),
            "bottomcenter" => (centerX, bottom),
            "leftcenter" => (left, centerY),
            "rightcenter" => (right, centerY),
            "center" => (centerX, centerY),
            _ => throw new ArgumentException($"Unknown position: {position}", nameof(position)),
        };
    }

    private static string Normalize(string position) =>
        new(position.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
