using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Geolocation;

/// <summary>
/// Device position. Request the "location" permission (permissions plugin) before calling, otherwise
/// the platform reports no fix.
/// </summary>
[CarbonPlugin("Geolocation", description: "Read the device's current position.")]
[CarbonPluginPlatform("android", "ios")]
[CarbonPermission("geolocation:default", "Allow reading the device position.", Commands = new[] { "geolocation:*" })]
public partial class GeolocationPlugin : IPlugin
{
    internal const int MinTimeoutMs = 500;
    internal const int MaxTimeoutMs = 60_000;

    private readonly IGeolocationProvider _provider;

    public GeolocationPlugin(AppHandle app)
        : this(app.Services.GetService<IGeolocationProvider>() ?? new UnsupportedGeolocationProvider()) { }

    // Injection seam for tests and for the native binding.
    internal GeolocationPlugin(IGeolocationProvider provider) => _provider = provider;

    public string Namespace => "geolocation";

    [CarbonCommand("current")]
    public Task<GeolocationPosition?> Current(CurrentPositionArgs args) =>
        _provider.GetCurrentAsync(ClampTimeout(args.TimeoutMs), Math.Max(0, args.MaximumAgeMs));

    /// <summary>Keeps a caller from hanging forever or polling with a nonsensical timeout.</summary>
    internal static int ClampTimeout(int timeoutMs) => Math.Clamp(timeoutMs, MinTimeoutMs, MaxTimeoutMs);
}
