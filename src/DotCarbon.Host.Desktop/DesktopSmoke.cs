using System.Globalization;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Enables deterministic launch checks when <c>CARBON_SMOKE=1</c>. The app follows its normal
/// startup path, prints machine-readable markers, and closes after the native loop begins.
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

        // Give the native loop time to process startup before requesting a close.
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
