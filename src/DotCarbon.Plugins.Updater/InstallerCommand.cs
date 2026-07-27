namespace DotCarbon.Plugins.Updater;

/// <summary>The desktop OS an installer command targets. Passed in so the builders are host-independent (testable).</summary>
internal enum InstallerOs
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// Builds the process invocation that runs an update installer. Pure and OS-parameterized so the
/// per-platform silent-install flags and the Windows install-on-exit relaunch can be unit-tested;
/// the plugin only spawns the result (which terminates the app, so the spawn itself is smoke-tested).
/// </summary>
internal static class InstallerCommand
{
    /// <summary>
    /// How to launch <paramref name="artifactPath"/> now. <paramref name="passive"/> selects the
    /// platform's silent/unattended flags where an installer supports them:
    /// <list type="bullet">
    /// <item>Windows <c>.msi</c> → <c>msiexec /i … /passive /norestart</c> (vs an interactive UI);</item>
    /// <item>Windows <c>.exe</c> (NSIS) → <c>/S</c> (vs the wizard);</item>
    /// <item>macOS <c>.dmg</c> → <c>open</c> (no silent story — Finder mounts it either way);</item>
    /// <item>Linux (AppImage) → run it directly.</item>
    /// </list>
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Args) For(string artifactPath, bool passive, InstallerOs os)
    {
        var extension = Path.GetExtension(artifactPath).ToLowerInvariant();
        return os switch
        {
            InstallerOs.Windows when extension == ".msi" =>
                ("msiexec", passive ? ["/i", artifactPath, "/passive", "/norestart"] : ["/i", artifactPath]),
            InstallerOs.Windows =>
                (artifactPath, passive ? ["/S"] : []),
            InstallerOs.MacOS =>
                ("open", [artifactPath]),
            _ =>
                (artifactPath, []),
        };
    }

    /// <summary>
    /// The detached helper that performs a Windows install-on-exit: wait for the running app
    /// (<paramref name="pid"/>) to close so its files unlock, run the installer, then relaunch the app.
    /// This is why an in-place MSI/NSIS update needs the app to exit first. Returned as an argument
    /// list for <c>powershell</c> so the caller need not hand-quote a nested command string.
    /// </summary>
    public static IReadOnlyList<string> WindowsRelaunchArgs(
        int pid, string installerFileName, IReadOnlyList<string> installerArgs, string exePath)
    {
        var installerArgList = installerArgs.Count == 0
            ? string.Empty
            : " -ArgumentList " + string.Join(",", installerArgs.Select(PowerShellQuote));

        var script =
            $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; " +
            $"Start-Process -Wait -FilePath {PowerShellQuote(installerFileName)}{installerArgList}; " +
            $"Start-Process -FilePath {PowerShellQuote(exePath)}";

        return ["-NoProfile", "-WindowStyle", "Hidden", "-Command", script];
    }

    /// <summary>Single-quotes a value for a PowerShell command, doubling any embedded single quotes.</summary>
    private static string PowerShellQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
