using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

/// <summary>
/// Task 8.8: `carbon bundle desktop` flag parity with `tauri build` — multi `--target`, `--debug`,
/// `--no-bundle`, and `--bundles` format selection. The format override is pure config manipulation,
/// so it is covered directly; the flags themselves were exercised against real builds.
/// </summary>
public class BundleFlagsTests
{
    [Fact]
    public void Bundles_override_replaces_the_macos_formats()
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "osx-arm64", ["app"]));

        Assert.Equal(["app"], config.Bundle.MacOS.Formats);
    }

    [Fact]
    public void Bundles_override_replaces_the_windows_formats()
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "win-x64", ["nsis"]));

        Assert.Equal(["nsis"], config.Bundle.Windows.Formats);
    }

    [Fact]
    public void Bundles_override_replaces_the_linux_formats()
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "linux-x64", ["deb", "rpm"]));

        Assert.Equal(["deb", "rpm"], config.Bundle.Linux.Formats);
    }

    [Theory]
    [InlineData("deb,rpm")]
    [InlineData("deb rpm")]
    public void Formats_may_be_comma_or_space_separated_in_one_token(string value)
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "linux-x64", [value]));

        Assert.Equal(["deb", "rpm"], config.Bundle.Linux.Formats);
    }

    [Theory]
    // Formats are per-OS, so the same word is valid on one platform and meaningless on another.
    [InlineData("osx-arm64", "msi")]
    [InlineData("win-x64", "dmg")]
    [InlineData("linux-x64", "nsis")]
    public void A_format_from_the_wrong_platform_is_an_error_not_a_silent_default(string target, string format)
    {
        // Silently producing the default set would look like success and ship the wrong artifact.
        var config = new CarbonConfig();

        Assert.False(BuildCommand.ApplyFormatOverride(config, target, [format]));
    }

    [Fact]
    public void An_unknown_format_is_rejected()
    {
        var config = new CarbonConfig();

        Assert.False(BuildCommand.ApplyFormatOverride(config, "linux-x64", ["pkgbuild"]));
    }

    [Fact]
    public void Duplicate_formats_collapse()
    {
        var config = new CarbonConfig();

        Assert.True(BuildCommand.ApplyFormatOverride(config, "win-x64", ["msi", "msi", "nsis"]));

        Assert.Equal(["msi", "nsis"], config.Bundle.Windows.Formats);
    }

    [Fact]
    public void MacOs_defaults_to_app_plus_dmg()
    {
        // The .app is what gets signed; the .dmg wraps it for distribution. Both by default.
        Assert.Equal(["app", "dmg"], new CarbonConfig().Bundle.MacOS.Formats);
    }

    [Fact]
    public void The_macos_formats_list_binds_from_carbon_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"carbon-mac-fmt-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "app": { "name": "X" }, "bundle": { "macOS": { "formats": ["app"] } } }""");
        try
        {
            Assert.Equal(["app"], ConfigLoader.Load(path).Bundle.MacOS.Formats);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
