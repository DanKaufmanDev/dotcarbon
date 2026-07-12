using System.Text.Json.Serialization;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

public sealed record DesktopNativeItemEvent(
    string Label,
    string Kind);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopNativeItemEvent))]
internal partial class DesktopNativeJsonContext : JsonSerializerContext;

internal static class DesktopNativeEventEmitter
{
    public static Action Create(AppHandle app, string eventName, string label, string kind) =>
        () =>
        {
            var task = app.EmitAsync(
                new CarbonEventName<DesktopNativeItemEvent>(eventName),
                new DesktopNativeItemEvent(label, kind),
                DesktopNativeJsonContext.Default.DesktopNativeItemEvent);

            if (!task.IsCompletedSuccessfully)
                _ = ObserveAsync(task, eventName);
        };

    private static async Task ObserveAsync(Task task, string eventName)
    {
        try { await task; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Native event '{eventName}' failed: {ex.Message}");
        }
    }
}
