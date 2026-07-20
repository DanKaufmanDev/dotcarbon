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

    [Fact]
    public void ExpandResource_handles_literals_dirs_and_globs_preserving_structure()
    {
        // Task 4.8: entries may be a literal file, a directory, or a glob; the relative path is kept so
        // the runtime can resolve resolveResource("assets/<name>") against the resource dir.
        var dir = Path.Combine(Path.GetTempPath(), "carbon-res-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "assets", "sub"));
            File.WriteAllText(Path.Combine(dir, "LICENSE"), "x");
            File.WriteAllText(Path.Combine(dir, "assets", "a.json"), "x");
            File.WriteAllText(Path.Combine(dir, "assets", "b.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "assets", "sub", "c.json"), "x");

            string[] Rel(IEnumerable<string> paths) =>
                paths.Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/')).OrderBy(p => p).ToArray();

            // Literal file.
            Assert.Equal(["LICENSE"], Rel(BuildCommand.ExpandResource(dir, "LICENSE")));
            // Directory → every file under it, recursively.
            Assert.Equal(["assets/a.json", "assets/b.txt", "assets/sub/c.json"], Rel(BuildCommand.ExpandResource(dir, "assets")));
            // Top-level glob does not descend.
            Assert.Equal(["assets/a.json"], Rel(BuildCommand.ExpandResource(dir, "assets/*.json")));
            // Recursive glob spans directories.
            Assert.Equal(["assets/a.json", "assets/sub/c.json"], Rel(BuildCommand.ExpandResource(dir, "assets/**/*.json")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CopyBundleResources_preserves_relative_paths_for_globs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "carbon-rescopy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "assets", "sub"));
            File.WriteAllText(Path.Combine(dir, "assets", "a.json"), "aa");
            File.WriteAllText(Path.Combine(dir, "assets", "sub", "c.json"), "cc");
            var dest = Path.Combine(dir, "dest");

            Assert.True(BuildCommand.CopyBundleResources(["assets/**/*.json"], dir, dest));

            // The "assets/..." structure is preserved under the destination, not flattened.
            Assert.Equal("aa", File.ReadAllText(Path.Combine(dest, "assets", "a.json")));
            Assert.Equal("cc", File.ReadAllText(Path.Combine(dest, "assets", "sub", "c.json")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
