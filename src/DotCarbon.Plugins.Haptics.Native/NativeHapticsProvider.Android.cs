#if ANDROID
using Android.Content;
using Android.OS;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Haptics;

namespace DotCarbon.Plugins.Haptics.Native;

/// <summary>
/// Android haptics via <see cref="Vibrator"/>. Impact/notification map onto short one-shot or pattern
/// vibrations, since Android has no direct equivalent of iOS's feedback generators. Requires the
/// VIBRATE manifest permission (a normal permission — no runtime prompt).
/// </summary>
internal sealed class NativeHapticsProvider : IHapticsProvider
{
    private readonly AppHandle _app;

    public NativeHapticsProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? global::Android.App.Application.Context;

    public Task ImpactAsync(string style)
    {
        // Heavier styles buzz longer and harder.
        var (duration, amplitude) = style switch
        {
            "light" => (10, 40),
            "heavy" => (30, 255),
            _ => (20, 128),
        };
        OneShot(duration, amplitude);
        return Task.CompletedTask;
    }

    public Task NotificationAsync(string type)
    {
        // Distinct rhythms so the three outcomes feel different.
        long[] pattern = type switch
        {
            "warning" => [0, 20, 80, 20],
            "error" => [0, 30, 60, 30, 60, 30],
            _ => [0, 15, 60, 25],
        };
        Pattern(pattern);
        return Task.CompletedTask;
    }

    public Task VibrateAsync(int durationMs)
    {
        OneShot(durationMs, -1 /* VibrationEffect.DefaultAmplitude */);
        return Task.CompletedTask;
    }

    private void OneShot(int durationMs, int amplitude)
    {
        var vibrator = GetVibrator();
        if (vibrator is null || !vibrator.HasVibrator) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            vibrator.Vibrate(VibrationEffect.CreateOneShot(durationMs, amplitude)!);
        else
            LegacyVibrate(vibrator, durationMs);
    }

    private void Pattern(long[] pattern)
    {
        var vibrator = GetVibrator();
        if (vibrator is null || !vibrator.HasVibrator) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            vibrator.Vibrate(VibrationEffect.CreateWaveform(pattern, -1)!);
        else
            LegacyPattern(vibrator, pattern);
    }

#pragma warning disable CA1422, CS0618 // pre-API-26 vibrate overloads
    private static void LegacyVibrate(Vibrator vibrator, int durationMs) => vibrator.Vibrate(durationMs);
    private static void LegacyPattern(Vibrator vibrator, long[] pattern) => vibrator.Vibrate(pattern, -1);
#pragma warning restore CA1422, CS0618

    private Vibrator? GetVibrator()
    {
        var context = Context;
        // API 31+ routes through VibratorManager; older releases expose the service directly.
        if (OperatingSystem.IsAndroidVersionAtLeast(31) &&
            context.GetSystemService(Context.VibratorManagerService) is VibratorManager manager)
            return manager.DefaultVibrator;

#pragma warning disable CA1422, CS0618
        return context.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422, CS0618
    }
}
#endif
