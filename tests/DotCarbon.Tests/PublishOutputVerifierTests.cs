using DotCarbon.Cli.Bundling;
using DotCarbon.Cli.Commands;
using DotCarbon.Core.Config;
using Xunit;

namespace DotCarbon.Tests;

public class PublishOutputVerifierTests
{
    [Fact]
    public void Single_file_publish_accepts_one_launcher()
    {
        var dir = TempOutput();
        var exe = Path.Combine(dir, "fixture.exe");
        File.WriteAllText(exe, "launcher");

        var result = PublishOutputVerifier.Verify(dir, "win-x64", allowSidecars: false);

        Assert.True(result.Success, result.Error);
        Assert.Equal(exe, result.ExecutablePath);
    }

    [Fact]
    public void Single_file_publish_rejects_sidecars()
    {
        var dir = TempOutput();
        File.WriteAllText(Path.Combine(dir, "fixture.exe"), "launcher");
        File.WriteAllText(Path.Combine(dir, "fixture.dll"), "sidecar");

        var result = PublishOutputVerifier.Verify(dir, "win-x64", allowSidecars: false);

        Assert.False(result.Success);
        Assert.Contains("extra files", result.Error);
    }

    [Fact]
    public void Aot_sidecar_mode_accepts_one_launcher_plus_native_files()
    {
        var dir = TempOutput();
        File.WriteAllText(Path.Combine(dir, "fixture.exe"), "launcher");
        File.WriteAllText(Path.Combine(dir, "Photino.Native.dll"), "native");

        var result = PublishOutputVerifier.Verify(dir, "win-x64", allowSidecars: true);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Publish_output_rejects_empty_launcher()
    {
        var dir = TempOutput();
        File.WriteAllBytes(Path.Combine(dir, "fixture.exe"), []);

        var result = PublishOutputVerifier.Verify(dir, "win-x64", allowSidecars: false);

        Assert.False(result.Success);
        Assert.Contains("empty", result.Error);
    }

    [Fact]
    public void Package_fixture_has_a_discoverable_carbon_shape()
    {
        var dir = Path.Combine(Fixtures.Dir, "package-app");
        var config = ConfigLoader.Load(Path.Combine(dir, "carbon.json"));

        Assert.True(File.Exists(Path.Combine(dir, config.Build.FrontendDist, "index.html")));
        Assert.Equal(
            Path.Combine(dir, "src-carbon", "PackageApp.csproj"),
            ProjectLocator.FindHostProject(dir, config));
    }

    [Fact]
    public void Windows_installer_wxs_uses_explicit_components()
    {
        var dir = TempOutput();
        var exe = Path.Combine(dir, "fixture.exe");
        File.WriteAllText(exe, "launcher");
        var nested = Path.Combine(dir, "resources");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "settings.json"), "{}");

        var config = new CarbonConfig
        {
            App = new AppConfig
            {
                Name = "Fixture",
                Version = "1.2.3",
                Identifier = "dev.dotcarbon.fixture",
            },
        };

        var wxs = BuildCommand.WindowsInstallerWxs(config, dir, exe, webView2: null, iconIco: null);

        Assert.DoesNotContain("<Files ", wxs);
        Assert.Contains("<Component Id=\"AppFile0\"", wxs);
        Assert.Contains("<File Id=\"AppFile0File\"", wxs);
        Assert.Contains("<Directory Id=\"AppDir0\" Name=\"resources\">", wxs);
        Assert.Contains("<Feature Id=\"MainFeature\"", wxs);
        Assert.Contains("<ComponentRef Id=\"AppFile0\" />", wxs);
    }

    private static string TempOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-publish-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
