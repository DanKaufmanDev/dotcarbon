using DotCarbon.Cli.Platforms;
using Xunit;

namespace DotCarbon.Tests;

public class MobilePlatformGeneratorTests
{
    [Fact]
    public void Mobile_smoke_can_generate_local_host_project_references()
    {
        var previous = Environment.GetEnvironmentVariable("CARBON_MOBILE_SMOKE_LOCAL_HOSTS");
        Environment.SetEnvironmentVariable("CARBON_MOBILE_SMOKE_LOCAL_HOSTS", "1");
        try
        {
            var repo = FindRepoRoot();
            var config = Fixtures.App(name: "MobileSmoke", version: "0.1.0", id: "dev.dotcarbon.mobilesmoke");
            var androidDir = Path.Combine(repo, "tests", "mobile-smoke", ".carbon", "platforms", "android");
            var iosDir = Path.Combine(repo, "tests", "mobile-smoke", ".carbon", "platforms", "ios");

            var androidProject = new AndroidPlatformGenerator()
                .Generate(new PlatformContext(config, Path.Combine(repo, "tests", "mobile-smoke"), androidDir))
                .Single(file => file.RelativePath.EndsWith(".Android.csproj", StringComparison.Ordinal))
                .Content;
            var iosProject = new IosPlatformGenerator()
                .Generate(new PlatformContext(config, Path.Combine(repo, "tests", "mobile-smoke"), iosDir))
                .Single(file => file.RelativePath.EndsWith(".iOS.csproj", StringComparison.Ordinal))
                .Content;

            Assert.Contains("ProjectReference Include=", androidProject);
            Assert.Contains(@"src\DotCarbon.Host.Android\DotCarbon.Host.Android.csproj", androidProject);
            Assert.DoesNotContain("PackageReference Include=\"DotCarbon.Host.Android\"", androidProject);

            Assert.Contains("ProjectReference Include=", iosProject);
            Assert.Contains(@"src\DotCarbon.Host.iOS\DotCarbon.Host.iOS.csproj", iosProject);
            Assert.DoesNotContain("PackageReference Include=\"DotCarbon.Host.iOS\"", iosProject);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CARBON_MOBILE_SMOKE_LOCAL_HOSTS", previous);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotCarbon.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find DotCarbon.slnx.");
    }
}
