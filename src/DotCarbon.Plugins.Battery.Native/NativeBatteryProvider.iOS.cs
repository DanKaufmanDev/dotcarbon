#if IOS
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Battery;
using UIKit;

namespace DotCarbon.Plugins.Battery.Native;

/// <summary>Reads the battery from iOS's <see cref="UIDevice"/> (no native handle needed — it is static).</summary>
internal sealed class NativeBatteryProvider : IBatteryProvider
{
    // The ctor matches the Android provider's shape so UseBattery() can construct either uniformly.
    public NativeBatteryProvider(AppHandle app) { }

    public BatteryStatus Read()
    {
        var device = UIDevice.CurrentDevice;
        device.BatteryMonitoringEnabled = true;

        var level = device.BatteryLevel; // 0..1, or -1 when unknown (e.g. the simulator)
        double? fraction = level >= 0 ? level : null;

        (bool? charging, string state) result = device.BatteryState switch
        {
            UIDeviceBatteryState.Charging => (true, "charging"),
            UIDeviceBatteryState.Full => (false, "full"),
            UIDeviceBatteryState.Unplugged => (false, "discharging"),
            _ => (null, "unknown"),
        };

        return new BatteryStatus(fraction, result.charging, result.state);
    }
}
#endif
