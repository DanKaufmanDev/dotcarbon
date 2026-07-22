namespace DotCarbon.Plugins.Battery;

/// <summary>
/// A snapshot of the device battery. <see cref="Level"/> is 0..1 (or null when unknown, e.g. a desktop
/// with no battery), and <see cref="State"/> is one of "charging" | "discharging" | "full" | "unknown".
/// </summary>
public record BatteryStatus(double? Level, bool? Charging, string State);

/// <summary>
/// Reads the battery from the current platform. The desktop plugin ships a best-effort implementation;
/// a mobile app registers a native one (Android <c>BatteryManager</c> / iOS <c>UIDevice</c>) via
/// <c>app.UseBattery()</c> from the <c>DotCarbon.Plugins.Battery.Native</c> package.
/// </summary>
public interface IBatteryProvider
{
    BatteryStatus Read();
}
