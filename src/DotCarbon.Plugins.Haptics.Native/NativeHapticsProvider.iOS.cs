#if IOS
using CoreFoundation;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Haptics;
using UIKit;

namespace DotCarbon.Plugins.Haptics.Native;

/// <summary>
/// iOS haptics via <see cref="UIImpactFeedbackGenerator"/> / <see cref="UINotificationFeedbackGenerator"/>.
/// Feedback generators are UIKit objects, so they are driven on the main queue. iOS has no
/// arbitrary-duration vibration API, so <c>vibrate</c> maps onto an impact of matching weight.
/// </summary>
internal sealed class NativeHapticsProvider : IHapticsProvider
{
    public NativeHapticsProvider(AppHandle app) { }

    public Task ImpactAsync(string style)
    {
        var weight = style switch
        {
            "light" => UIImpactFeedbackStyle.Light,
            "heavy" => UIImpactFeedbackStyle.Heavy,
            _ => UIImpactFeedbackStyle.Medium,
        };
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            using var generator = new UIImpactFeedbackGenerator(weight);
            generator.Prepare();
            generator.ImpactOccurred();
        });
        return Task.CompletedTask;
    }

    public Task NotificationAsync(string type)
    {
        var feedback = type switch
        {
            "warning" => UINotificationFeedbackType.Warning,
            "error" => UINotificationFeedbackType.Error,
            _ => UINotificationFeedbackType.Success,
        };
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            using var generator = new UINotificationFeedbackGenerator();
            generator.Prepare();
            generator.NotificationOccurred(feedback);
        });
        return Task.CompletedTask;
    }

    // No arbitrary-duration vibration exists on iOS; approximate with a weighted impact.
    public Task VibrateAsync(int durationMs) =>
        ImpactAsync(durationMs >= 200 ? "heavy" : durationMs >= 60 ? "medium" : "light");
}
#endif
