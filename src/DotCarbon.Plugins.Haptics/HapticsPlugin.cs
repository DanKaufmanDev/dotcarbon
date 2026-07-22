using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Haptics;

/// <summary>
/// Haptic feedback. Mobile-only in practice: a mobile app calls <c>app.UseHaptics()</c> to register the
/// native provider; on desktop the calls are no-ops so shared frontend code needs no platform checks.
/// </summary>
[CarbonPlugin("Haptics", description: "Play haptic feedback (impact, notification, vibrate).")]
[CarbonPluginPlatform("android", "ios")]
[CarbonPermission("haptics:default", "Allow all haptics commands.", Commands = new[] { "haptics:*" })]
public partial class HapticsPlugin : IPlugin
{
    /// <summary>Vibration longer than this is almost certainly a bug, so it is clamped.</summary>
    internal const int MaxDurationMs = 5000;

    private readonly IHapticsProvider _provider;

    public HapticsPlugin(AppHandle app)
        : this(app.Services.GetService<IHapticsProvider>() ?? new NoopHapticsProvider()) { }

    // Injection seam for tests and for the native binding.
    internal HapticsPlugin(IHapticsProvider provider) => _provider = provider;

    public string Namespace => "haptics";

    [CarbonCommand("impact")]
    public Task Impact(ImpactArgs args) => _provider.ImpactAsync(Normalize(args.Style, "medium"));

    [CarbonCommand("notification")]
    public Task Notification(NotificationArgs args) => _provider.NotificationAsync(Normalize(args.Type, "success"));

    [CarbonCommand("vibrate")]
    public Task Vibrate(VibrateArgs args) => _provider.VibrateAsync(ClampDuration(args.DurationMs));

    /// <summary>Lower-cases and trims a style/type, falling back when it is blank.</summary>
    internal static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    internal static int ClampDuration(int durationMs) => Math.Clamp(durationMs, 1, MaxDurationMs);
}
