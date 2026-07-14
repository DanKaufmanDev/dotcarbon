using System.Globalization;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Headless launch verification. When <c>CARBON_SMOKE=1</c> the app boots exactly as normal —
/// creating its window(s), tray, menu, plugins, and the JS bridge — prints machine-readable markers,
/// then closes itself. That lets CI on each OS install the packaged app, launch it, and assert it
/// actually starts (not just that the installer file exists). Completely inert without the env var,
/// so it never affects a shipped app.
///
/// Markers (grep-able): <c>[[CARBON_SMOKE]] boot host=… plugins=… windows=…</c> and
/// <c>[[CARBON_SMOKE]] exit ok</c>. Tray/menu backends print their own "ready" lines when they run.
/// </summary>
internal static class DesktopSmoke
{
    private const string EnableVar = "CARBON_SMOKE";
    private const string DelayVar = "CARBON_SMOKE_MS";
    private const int DefaultDelayMs = 2500;

    public static bool Enabled =>
        Environment.GetEnvironmentVariable(EnableVar) is "1" or "true" or "TRUE";

    public static Action<CarbonLifecycleEvent> CreateLifecycleHandler()
    {
        var armed = 0;
        return lifecycleEvent =>
        {
            if (lifecycleEvent.Kind != CarbonLifecycleEventKind.WindowCreated ||
                lifecycleEvent.Window?.Label != lifecycleEvent.App.Config.Window.Label ||
                Interlocked.Exchange(ref armed, 1) != 0)
                return;

            Arm(lifecycleEvent.App);
        };
    }

    private static void Arm(AppHandle handle)
    {
        Console.WriteLine(
            $"[[CARBON_SMOKE]] boot host={HostName()} plugins={handle.Plugins.Count} windows={handle.Windows.Count}");
        Console.Out.Flush();

        // The native main window now exists. Let its message loop pump before closing it.
        _ = Task.Run(async () =>
        {
            await Task.Delay(ReadDelay());
            try
            {
                handle.Exit();
                Console.WriteLine("[[CARBON_SMOKE]] exit ok");
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[[CARBON_SMOKE]] exit failed: {ex.Message}");
                Environment.Exit(1);
            }
        });
    }

    private static int ReadDelay() =>
        int.TryParse(Environment.GetEnvironmentVariable(DelayVar), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? ms
            : DefaultDelayMs;

    private static string HostName() =>
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";
}
