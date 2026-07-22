namespace DotCarbon.Plugins.Haptics;

/// <summary>
/// Desktop fallback: desktops have no haptic hardware, so the calls succeed and do nothing. That lets a
/// shared frontend fire haptics on every platform without branching.
/// </summary>
internal sealed class NoopHapticsProvider : IHapticsProvider
{
    public Task ImpactAsync(string style) => Task.CompletedTask;
    public Task NotificationAsync(string type) => Task.CompletedTask;
    public Task VibrateAsync(int durationMs) => Task.CompletedTask;
}
