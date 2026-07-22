#if ANDROID
using Android.Content;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Battery;

namespace DotCarbon.Plugins.Battery.Native;

/// <summary>
/// Reads the battery from Android's sticky <c>ACTION_BATTERY_CHANGED</c> broadcast, using the
/// <see cref="Context"/> exposed by the Android host through <see cref="AppHandle.PlatformNativeHandle"/>.
/// </summary>
internal sealed class NativeBatteryProvider : IBatteryProvider
{
    private readonly AppHandle _app;

    public NativeBatteryProvider(AppHandle app) => _app = app;

    public BatteryStatus Read()
    {
        var context = _app.PlatformNativeHandle as Context
            ?? global::Android.App.Application.Context;

        using var filter = new IntentFilter(Intent.ActionBatteryChanged);
        // A null receiver just samples the current sticky broadcast (no exported-flag needed).
        using var intent = context.RegisterReceiver(null, filter);
        if (intent is null) return new BatteryStatus(null, null, "unknown");

        var level = intent.GetIntExtra(global::Android.OS.BatteryManager.ExtraLevel, -1);
        var scale = intent.GetIntExtra(global::Android.OS.BatteryManager.ExtraScale, -1);
        double? fraction = level >= 0 && scale > 0 ? (double)level / scale : null;

        var status = intent.GetIntExtra(global::Android.OS.BatteryManager.ExtraStatus, -1);
        (bool? charging, string state) result =
            status == (int)global::Android.OS.BatteryStatus.Charging ? (true, "charging") :
            status == (int)global::Android.OS.BatteryStatus.Full ? (false, "full") :
            status == (int)global::Android.OS.BatteryStatus.Discharging
                || status == (int)global::Android.OS.BatteryStatus.NotCharging ? (false, "discharging") :
            (null, "unknown");

        return new BatteryStatus(fraction, result.charging, result.state);
    }
}
#endif
