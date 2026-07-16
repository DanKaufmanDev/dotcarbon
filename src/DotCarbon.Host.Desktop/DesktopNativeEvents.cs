using System.Text.Json.Serialization;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

public sealed record DesktopNativeItemEvent(
    string Label,
    string Kind);

// Tray pointer events serialize their enums as names ("Click", "Left") rather than integers, so the
// frontend can switch on them the way Tauri's TrayIconEvent does.
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DesktopNativeItemEvent))]
[JsonSerializable(typeof(CarbonTrayEvent))]
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

/// <summary>
/// Forwards tray pointer events (Task 2.8) to the frontend. Separate from the item emitter because
/// these carry the pointer payload rather than a clicked item's label.
/// </summary>
internal static class DesktopTrayEventEmitter
{
    public static Action<CarbonTrayEvent> Create(AppHandle app, string eventName) =>
        trayEvent =>
        {
            // Move events fire continuously while the pointer is over the icon, so this runs hot —
            // emit without awaiting and only pay for error observation when something actually fails.
            var task = app.EmitAsync(
                new CarbonEventName<CarbonTrayEvent>(eventName),
                trayEvent,
                DesktopNativeJsonContext.Default.CarbonTrayEvent);

            if (!task.IsCompletedSuccessfully) _ = ObserveAsync(task, eventName);
        };

    private static async Task ObserveAsync(Task task, string eventName)
    {
        try { await task; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray event '{eventName}' failed: {ex.Message}");
        }
    }
}
