using DotCarbon.Cli.Commands;
using Xunit;

namespace DotCarbon.Tests;

public class BuildCommandTests
{
    [Fact]
    public async Task BuildFrontend_uses_existing_dist_when_no_package_json_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dist = Path.Combine(dir, "ui", "dist");
            Directory.CreateDirectory(dist);
            File.WriteAllText(Path.Combine(dist, "index.html"), "<!doctype html><title>ready</title>");

            var config = Fixtures.App();
            config.Build.FrontendDist = "ui/dist";

            Assert.True(await BuildCommand.BuildFrontend(config, dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
