using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

public class ConfigTests
{
    [Theory]
    [InlineData("minimal.carbon.json")]
    [InlineData("plugin-heavy.carbon.json")]
    [InlineData("multi-window.carbon.json")]
    [InlineData("mobile-safe.carbon.json")]
    public void Fixture_apps_parse_and_have_no_config_errors(string fixture)
    {
        var config = Fixtures.Load(fixture);
        Assert.False(string.IsNullOrWhiteSpace(config.App.Name));
        Assert.DoesNotContain(ConfigValidator.Validate(config), i => i.Severity == ConfigSeverity.Error);
    }

    [Fact]
    public void Parses_app_bundle_and_permissions()
    {
        var config = Fixtures.Load("mobile-safe.carbon.json");

        Assert.Equal("Mobile Safe", config.App.Name);
        Assert.Equal("Mobile", config.App.DisplayName);
        Assert.Equal("com.example.mobile", config.App.Identifier);
        Assert.Equal(new[] { "desktop", "android", "ios" }, config.Bundle.Targets);
        Assert.Equal(26, config.Bundle.Android.MinSdk);
        Assert.Equal("16.0", config.Bundle.Ios.MinimumOSVersion);
        Assert.True(config.Permissions.Camera);
        Assert.Equal("Scan receipts", config.Permissions.Descriptions["camera"]);
    }

    [Fact]
    public void Parses_additional_windows()
    {
        var config = Fixtures.Load("multi-window.carbon.json");
        Assert.Single(config.Windows);
        Assert.Equal("settings", config.Windows[0].Label);
        Assert.Equal(480, config.Windows[0].Width);
    }

    [Fact]
    public void Valid_config_reports_no_issues()
    {
        Assert.Empty(ConfigValidator.Validate(Fixtures.App()));
    }

    [Fact]
    public void Invalid_identifier_is_an_error()
    {
        var config = Fixtures.App(id: "not-reverse-dns");
        Assert.Contains(ConfigValidator.Validate(config),
            i => i.Severity == ConfigSeverity.Error && i.Path == "app.identifier");
    }

    [Fact]
    public void Unknown_bundle_target_is_an_error()
    {
        var config = Fixtures.App();
        config.Bundle.Targets = ["desktop", "watchos"];
        Assert.Contains(ConfigValidator.Validate(config),
            i => i.Severity == ConfigSeverity.Error && i.Path == "bundle.targets");
    }

    [Fact]
    public void Non_semver_version_is_a_warning()
    {
        var config = Fixtures.App(version: "beta");
        Assert.Contains(ConfigValidator.Validate(config),
            i => i.Severity == ConfigSeverity.Warning && i.Path == "app.version");
    }

    [Fact]
    public void MinSdk_greater_than_targetSdk_is_a_warning()
    {
        var config = Fixtures.App();
        config.Bundle.Android.MinSdk = 34;
        config.Bundle.Android.TargetSdk = 26;
        Assert.Contains(ConfigValidator.Validate(config),
            i => i.Severity == ConfigSeverity.Warning && i.Path == "bundle.android");
    }

    [Fact]
    public void Invalid_files_permission_scope_is_an_error()
    {
        var config = Fixtures.App();
        config.Permissions.Files = "everywhere";
        Assert.Contains(ConfigValidator.Validate(config),
            i => i.Severity == ConfigSeverity.Error && i.Path == "permissions.files");
    }
}
