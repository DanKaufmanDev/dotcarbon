namespace DotCarbon.Plugins.Haptics;

/// <summary>Impact feedback strength: "light" | "medium" | "heavy" (anything else is treated as medium).</summary>
public record ImpactArgs(string Style = "medium");

/// <summary>Notification feedback: "success" | "warning" | "error" (anything else is treated as success).</summary>
public record NotificationArgs(string Type = "success");

/// <summary>A plain buzz of the given length, clamped to a sensible maximum.</summary>
public record VibrateArgs(int DurationMs = 100);

/// <summary>
/// Plays haptic feedback on the device. Haptics are a mobile capability: a mobile app registers the
/// native provider (Android <c>Vibrator</c> / iOS <c>UIFeedbackGenerator</c>) via <c>app.UseHaptics()</c>
/// from <c>DotCarbon.Plugins.Haptics.Native</c>. Desktop has no haptics, so it is a no-op there rather
/// than an error — shared frontend code can call it unconditionally.
/// </summary>
public interface IHapticsProvider
{
    Task ImpactAsync(string style);
    Task NotificationAsync(string type);
    Task VibrateAsync(int durationMs);
}
