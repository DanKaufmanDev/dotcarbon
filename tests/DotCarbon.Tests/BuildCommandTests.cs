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

    [Theory]
    [InlineData("osx-arm64", "aarch64-apple-darwin")]
    [InlineData("osx-x64", "x86_64-apple-darwin")]
    [InlineData("win-x64", "x86_64-pc-windows-msvc")]
    [InlineData("linux-x64", "x86_64-unknown-linux-gnu")]
    public void TargetTriple_maps_targets_to_rust_triples(string target, string expected)
    {
        Assert.Equal(expected, BuildCommand.TargetTriple(target));
    }

    [Fact]
    public void CopyExternalBinaries_picks_the_target_variant_and_drops_the_triple()
    {
        // Task 4.7: the developer ships per-target variants; the bundler copies the one matching the
        // target next to the app, renamed to the entry's leaf so the shell plugin resolves it there.
        var dir = Path.Combine(Path.GetTempPath(), "carbon-extbin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var binaries = Path.Combine(dir, "binaries");
            Directory.CreateDirectory(binaries);
            File.WriteAllText(Path.Combine(binaries, "my-tool-aarch64-apple-darwin"), "arm");
            File.WriteAllText(Path.Combine(binaries, "my-tool-x86_64-apple-darwin"), "intel");

            var dest = Path.Combine(dir, "dest");

            Assert.True(BuildCommand.CopyExternalBinaries(["binaries/my-tool"], dir, dest, "osx-arm64"));

            var copied = Path.Combine(dest, "my-tool");
            Assert.True(File.Exists(copied));
            Assert.Equal("arm", File.ReadAllText(copied));
            // Only the target's variant lands, and the triple is gone from the name.
            Assert.False(File.Exists(Path.Combine(dest, "my-tool-aarch64-apple-darwin")));
            Assert.False(File.Exists(Path.Combine(dest, "my-tool-x86_64-apple-darwin")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CopyExternalBinaries_fails_when_the_variant_is_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-extbin-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            // No binaries/my-tool-<triple> provided → bundling must fail rather than ship nothing.
            Assert.False(BuildCommand.CopyExternalBinaries(["binaries/my-tool"], dir, Path.Combine(dir, "dest"), "osx-arm64"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
