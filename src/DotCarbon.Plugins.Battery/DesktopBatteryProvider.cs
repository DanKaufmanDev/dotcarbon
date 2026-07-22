using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DotCarbon.Plugins.Battery;

/// <summary>
/// Best-effort desktop battery reader. macOS is read from <c>pmset -g batt</c>; Windows and Linux are
/// not implemented in this reference plugin and report "unknown" (a real plugin would add
/// <c>GetSystemPowerStatus</c> / <c>/sys/class/power_supply</c>).
/// </summary>
internal sealed class DesktopBatteryProvider : IBatteryProvider
{
    public BatteryStatus Read() =>
        OperatingSystem.IsMacOS()
            ? ParsePmset(RunPmset())
            : new BatteryStatus(Level: null, Charging: null, State: "unknown");

    // `pmset -g batt` prints, e.g.:
    //   Now drawing from 'AC Power'
    //    -InternalBattery-0 (id=…)  100%; charged; 0:00 remaining present: true
    //   Now drawing from 'Battery Power'
    //    -InternalBattery-0 (id=…)  83%; discharging; 4:12 remaining present: true
    internal static BatteryStatus ParsePmset(string output)
    {
        double? level = null;
        var percent = Regex.Match(output, @"(\d+)%");
        if (percent.Success && int.TryParse(percent.Groups[1].Value, out var value))
            level = Math.Clamp(value / 100.0, 0.0, 1.0);

        var text = output.ToLowerInvariant();
        // "discharging" contains "charging", so test it first.
        var (charging, state) =
            text.Contains("discharging") ? ((bool?)false, "discharging") :
            text.Contains("charging") ? ((bool?)true, "charging") :
            text.Contains("charged") || text.Contains("full") ? ((bool?)false, "full") :
            ((bool?)null, "unknown");

        return new BatteryStatus(level, charging, state);
    }

    private static string RunPmset()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/pmset", "-g batt")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
