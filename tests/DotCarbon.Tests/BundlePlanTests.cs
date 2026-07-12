using DotCarbon.Cli.Bundling;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

public class BundlePlanTests
{
    private static BundleContext Ctx(CarbonConfig config, string target, bool package = true) =>
        new(config, "/tmp/app", ProjectDir: null, target,
            Aot: false, Package: package, UpdaterArtifacts: false, DryRun: true);

    [Fact]
    public void Mac_plan_packages_a_dmg_and_writes_a_manifest()
    {
        var plan = new DesktopBundler().Plan(Ctx(Fixtures.App(), "osx-arm64"));

        var package = plan.Steps.Single(s => s.Title == "Package installer");
        Assert.False(package.Skipped);
        Assert.Contains(".dmg", package.Detail);
        Assert.Contains(plan.Steps, s => s.Title == "Write build manifest");
    }

    [Fact]
    public void Manifest_step_references_the_target_output_path()
    {
        var plan = new DesktopBundler().Plan(Ctx(Fixtures.App(), "osx-arm64"));
        var manifest = plan.Steps.Single(s => s.Title == "Write build manifest");
        Assert.Contains("out/osx-arm64/", manifest.Detail);
    }

    [Fact]
    public void NoPackage_skips_installer_and_signing()
    {
        var plan = new DesktopBundler().Plan(Ctx(Fixtures.App(), "osx-arm64", package: false));

        Assert.True(plan.Steps.Single(s => s.Title == "Package installer").Skipped);
        Assert.All(plan.Steps.Where(s => s.Title.StartsWith("Code sign")), s => Assert.True(s.Skipped));
    }

    [Fact]
    public void Linux_plan_reflects_configured_formats()
    {
        var config = Fixtures.App();
        config.Bundle.Linux.Formats = ["deb", "rpm"];

        var plan = new DesktopBundler().Plan(Ctx(config, "linux-x64"));
        var package = plan.Steps.Single(s => s.Title == "Package installer");

        Assert.Contains(".deb", package.Detail);
        Assert.Contains(".rpm", package.Detail);
        Assert.DoesNotContain(".AppImage", package.Detail);
    }

    [Fact]
    public void Windows_signing_is_skipped_without_a_certificate()
    {
        var plan = new DesktopBundler().Plan(Ctx(Fixtures.App(), "win-x64"));
        Assert.True(plan.Steps.Single(s => s.Title.StartsWith("Code sign")).Skipped);
    }

    [Fact]
    public void Mac_signing_is_active_when_an_identity_is_configured()
    {
        var config = Fixtures.App();
        config.Bundle.MacOS.SigningIdentity = "Developer ID Application: Example";

        var plan = new DesktopBundler().Plan(Ctx(config, "osx-arm64"));
        Assert.False(plan.Steps.Single(s => s.Title.StartsWith("Code sign")).Skipped);
    }
}
