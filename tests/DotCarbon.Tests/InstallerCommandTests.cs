using DotCarbon.Plugins.Updater;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 9.6: passive (silent) install and the Windows install-on-exit relaunch. The command
/// construction is pure and OS-parameterized, so it is covered here; actually spawning the installer
/// terminates the process, so that is smoke-tested on real platforms.
/// </summary>
public class InstallerCommandTests
{
    [Fact]
    public void Windows_msi_passive_uses_unattended_flags()
    {
        var (fileName, args) = InstallerCommand.For(@"C:\updates\App.msi", passive: true, InstallerOs.Windows);

        Assert.Equal("msiexec", fileName);
        Assert.Equal(["/i", @"C:\updates\App.msi", "/passive", "/norestart"], args);
    }

    [Fact]
    public void Windows_msi_prompted_shows_the_installer_ui()
    {
        var (fileName, args) = InstallerCommand.For(@"C:\updates\App.msi", passive: false, InstallerOs.Windows);

        Assert.Equal("msiexec", fileName);
        Assert.Equal(["/i", @"C:\updates\App.msi"], args);
        Assert.DoesNotContain("/passive", args);
    }

    [Fact]
    public void Windows_nsis_exe_passive_uses_the_silent_switch()
    {
        var (fileName, args) = InstallerCommand.For(@"C:\updates\App-setup.exe", passive: true, InstallerOs.Windows);

        // NSIS's silent flag is /S; the exe is run directly.
        Assert.Equal(@"C:\updates\App-setup.exe", fileName);
        Assert.Equal(["/S"], args);
    }

    [Fact]
    public void Windows_nsis_exe_prompted_runs_the_wizard()
    {
        var (fileName, args) = InstallerCommand.For(@"C:\updates\App-setup.exe", passive: false, InstallerOs.Windows);

        Assert.Equal(@"C:\updates\App-setup.exe", fileName);
        Assert.Empty(args);
    }

    [Fact]
    public void MacOS_opens_the_dmg_regardless_of_mode()
    {
        // A .dmg has no silent install; Finder mounts it either way, so passive is a no-op here.
        foreach (var passive in new[] { true, false })
        {
            var (fileName, args) = InstallerCommand.For("/updates/App.dmg", passive, InstallerOs.MacOS);
            Assert.Equal("open", fileName);
            Assert.Equal(["/updates/App.dmg"], args);
        }
    }

    [Fact]
    public void Linux_runs_the_appimage_directly()
    {
        var (fileName, args) = InstallerCommand.For("/updates/App.AppImage", passive: true, InstallerOs.Linux);

        Assert.Equal("/updates/App.AppImage", fileName);
        Assert.Empty(args);
    }

    [Fact]
    public void Windows_relaunch_waits_for_the_app_then_installs_then_reopens()
    {
        var args = InstallerCommand.WindowsRelaunchArgs(
            pid: 4321,
            installerFileName: "msiexec",
            installerArgs: ["/i", @"C:\updates\App.msi", "/passive", "/norestart"],
            exePath: @"C:\Program Files\App\App.exe");

        // The whole sequence is one -Command script: wait for our PID, run the installer, relaunch.
        var script = Assert.Single(args, a => a.Contains("Wait-Process"));
        Assert.Contains("Wait-Process -Id 4321", script);
        Assert.Contains("Start-Process -Wait -FilePath 'msiexec'", script);
        Assert.Contains("/passive", script);
        // The relaunch comes after the install and points at the running app's own exe.
        Assert.Contains(@"Start-Process -FilePath 'C:\Program Files\App\App.exe'", script);
        Assert.True(
            script.IndexOf("Wait-Process", StringComparison.Ordinal)
                < script.IndexOf(@"App.exe'", StringComparison.Ordinal),
            "relaunch must come after waiting for exit");
        Assert.Contains("-WindowStyle", args);
    }

    [Fact]
    public void Windows_relaunch_single_quotes_are_escaped()
    {
        // A path with an apostrophe must not break out of the PowerShell single-quoted string.
        var args = InstallerCommand.WindowsRelaunchArgs(
            pid: 1, installerFileName: "msiexec", installerArgs: [],
            exePath: @"C:\Users\O'Brien\App.exe");

        var script = args[^1];
        Assert.Contains("'C:\\Users\\O''Brien\\App.exe'", script);
    }
}
