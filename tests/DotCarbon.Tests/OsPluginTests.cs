using DotCarbon.Plugins.Os;
using Xunit;

namespace DotCarbon.Tests;

public class OsPluginTests
{
    private static readonly OsPlugin Plugin = new();

    [Fact]
    public void Platform_is_a_known_value()
    {
        Assert.Contains(Plugin.Platform(), new[] { "macos", "windows", "linux", "android", "ios" });
    }

    [Fact]
    public void Arch_is_a_known_value()
    {
        Assert.Contains(Plugin.Arch(), new[] { "x86_64", "x86", "arm", "aarch64" });
    }

    [Fact]
    public void Family_matches_platform()
    {
        var expected = OperatingSystem.IsWindows() ? "windows" : "unix";
        Assert.Equal(expected, Plugin.Family());
    }

    [Fact]
    public void Info_populates_every_field()
    {
        var info = Plugin.Info();

        Assert.Equal(Plugin.Platform(), info.Platform);
        Assert.Equal(Plugin.Arch(), info.Arch);
        Assert.Equal(Plugin.Family(), info.Family);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.False(string.IsNullOrWhiteSpace(info.Hostname));
        Assert.Equal(Environment.NewLine, info.Eol);
        Assert.Equal(OperatingSystem.IsWindows() ? ".exe" : string.Empty, info.ExeExtension);
    }
}
